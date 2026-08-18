using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.World;

/// <summary>
/// Performs continuous circle sweeps against the streamed WLDX navigation grid.
/// Blocked records occupy their authored unit tile (<c>x..x+1, y..y+1</c>), and
/// rounded-corner sweeps let the actor slide naturally without treating it as a box.
/// </summary>
public sealed class WorldCollisionResolver(
    WorldStreamer worldStreamer,
    Func<IndoorTileGroup?> activeIndoorGroup)
{
    public const float CharacterRadius = 0.28f;

    private const float ContactSkin = 0.001f;
    private const float ContactApproachEpsilon = 0.000001f;
    private const int MaximumSlideIterations = 4;

    private readonly Dictionary<SectorCoord, Sector> _sectors = new(capacity: 9);
    private readonly List<IndoorTileGroup> _indoorGroups = [];
    private VisibleWorld? _cachedWorld;

    public WorldCollisionResolver(WorldStreamer worldStreamer)
        : this(worldStreamer, static () => null)
    {
    }

    public Vector2 ResolveMovement(Vector2 start, Vector2 intendedEnd)
    {
        RefreshSectorIndex();

        var position = start;
        var remaining = intendedEnd - start;
        for (var iteration = 0; iteration < MaximumSlideIterations; iteration++)
        {
            var remainingLength = remaining.Length();
            if (remainingLength <= float.Epsilon)
                break;

            if (!TryFindFirstHit(position, remaining, out var hit))
            {
                position += remaining;
                break;
            }

            var safeTime = MathF.Max(0.0f, hit.Time - ContactSkin / remainingLength);
            position += remaining * safeTime;

            var residual = remaining * (1.0f - safeTime);
            var intoSurface = Vector2.Dot(residual, hit.Normal);
            if (intoSurface < 0.0f)
                residual -= hit.Normal * intoSurface;
            remaining = residual;
        }

        return position;
    }

    /// <summary>Tests a segment with the same continuous sweep used for actor movement.</summary>
    public bool CanMoveDirectly(Vector2 start, Vector2 intendedEnd) =>
        Vector2.DistanceSquared(ResolveMovement(start, intendedEnd), intendedEnd) <= ContactSkin * ContactSkin;

    /// <summary>Returns whether the actor circle can stand at a position with navigation data loaded.</summary>
    public bool CanOccupy(Vector2 position)
    {
        RefreshSectorIndex();
        var minimumTileX = (int)MathF.Floor(position.X - CharacterRadius);
        var maximumTileX = (int)MathF.Floor(position.X + CharacterRadius);
        var minimumTileY = (int)MathF.Floor(position.Y - CharacterRadius);
        var maximumTileY = (int)MathF.Floor(position.Y + CharacterRadius);
        var radiusSquared = CharacterRadius * CharacterRadius;

        for (var tileY = minimumTileY; tileY <= maximumTileY; tileY++)
        for (var tileX = minimumTileX; tileX <= maximumTileX; tileX++)
        {
            if (!IsMovementBlockedFromCache(tileX, tileY))
                continue;

            var nearestX = Math.Clamp(position.X, tileX, tileX + 1.0f);
            var nearestY = Math.Clamp(position.Y, tileY, tileY + 1.0f);
            if (Vector2.DistanceSquared(position, new Vector2(nearestX, nearestY)) < radiusSquared)
                return false;
        }

        return true;
    }

    /// <summary>Returns the authored state. A tile outside the streamed world is reported as unknown/clear.</summary>
    public bool IsBlocked(int worldTileX, int worldTileY)
    {
        RefreshSectorIndex();
        if (TryGetIndoorTile(worldTileX, worldTileY, out var indoorGroup, out var indoorX, out var indoorY))
            return indoorGroup.Pathing.IsBlocked(indoorX, indoorY);

        return TryGetSectorTile(worldTileX, worldTileY, out var sector, out var localX, out var localY) &&
               sector.Pathing.IsBlocked(localX, localY);
    }

    /// <summary>
    /// Returns whether a tile stops movement. Missing streamed data is solid so the actor and
    /// A* cannot outrun the world streamer and wander into an unloaded void.
    /// </summary>
    public bool IsMovementBlocked(int worldTileX, int worldTileY)
    {
        RefreshSectorIndex();
        return IsMovementBlockedFromCache(worldTileX, worldTileY);
    }

    private bool TryFindFirstHit(Vector2 start, Vector2 delta, out SweepHit firstHit)
    {
        firstHit = default;
        var found = false;
        var minimumTileX = (int)MathF.Floor(MathF.Min(start.X, start.X + delta.X) - CharacterRadius);
        var maximumTileX = (int)MathF.Floor(MathF.Max(start.X, start.X + delta.X) + CharacterRadius);
        var minimumTileY = (int)MathF.Floor(MathF.Min(start.Y, start.Y + delta.Y) - CharacterRadius);
        var maximumTileY = (int)MathF.Floor(MathF.Max(start.Y, start.Y + delta.Y) + CharacterRadius);

        for (var tileY = minimumTileY; tileY <= maximumTileY; tileY++)
        for (var tileX = minimumTileX; tileX <= maximumTileX; tileX++)
        {
            if (!IsMovementBlockedFromCache(tileX, tileY))
                continue;

            var minimum = new Vector2(tileX, tileY);
            var maximum = minimum + Vector2.One;
            if (!TrySweepCircleAgainstBox(start, delta, minimum, maximum, out var hit))
                continue;

            if (!found || hit.Time < firstHit.Time)
            {
                found = true;
                firstHit = hit;
            }
        }

        return found;
    }

    private static bool TrySweepCircleAgainstBox(
        Vector2 start,
        Vector2 delta,
        Vector2 minimum,
        Vector2 maximum,
        out SweepHit firstHit)
    {
        var bestHit = default(SweepHit);
        var found = false;

        if (delta.X > 0.0f)
            TryAddFaceHit((minimum.X - CharacterRadius - start.X) / delta.X, -Vector2.UnitX, true);
        else if (delta.X < 0.0f)
            TryAddFaceHit((maximum.X + CharacterRadius - start.X) / delta.X, Vector2.UnitX, true);

        if (delta.Y > 0.0f)
            TryAddFaceHit((minimum.Y - CharacterRadius - start.Y) / delta.Y, -Vector2.UnitY, false);
        else if (delta.Y < 0.0f)
            TryAddFaceHit((maximum.Y + CharacterRadius - start.Y) / delta.Y, Vector2.UnitY, false);

        TryAddCornerHit(minimum, requireMaximumX: true, requireMaximumY: true);
        TryAddCornerHit(new Vector2(maximum.X, minimum.Y), requireMaximumX: false, requireMaximumY: true);
        TryAddCornerHit(new Vector2(minimum.X, maximum.Y), requireMaximumX: true, requireMaximumY: false);
        TryAddCornerHit(maximum, requireMaximumX: false, requireMaximumY: false);
        firstHit = bestHit;
        return found;

        void TryAddFaceHit(float time, Vector2 normal, bool verticalFace)
        {
            if (time < 0.0f || time > 1.0f || found && time >= bestHit.Time)
                return;

            var position = start + delta * time;
            var alongFace = verticalFace ? position.Y : position.X;
            var faceMinimum = verticalFace ? minimum.Y : minimum.X;
            var faceMaximum = verticalFace ? maximum.Y : maximum.X;
            if (alongFace < faceMinimum || alongFace > faceMaximum)
                return;

            bestHit = new SweepHit(time, normal);
            found = true;
        }

        void TryAddCornerHit(Vector2 corner, bool requireMaximumX, bool requireMaximumY)
        {
            var offset = start - corner;
            var a = delta.LengthSquared();
            var b = 2.0f * Vector2.Dot(offset, delta);
            var c = offset.LengthSquared() - CharacterRadius * CharacterRadius;
            var discriminant = b * b - 4.0f * a * c;
            if (discriminant < 0.0f || a <= float.Epsilon)
                return;

            var time = (-b - MathF.Sqrt(discriminant)) / (2.0f * a);
            if (time < 0.0f || time > 1.0f || found && time >= bestHit.Time)
                return;

            var position = start + delta * time;
            if (requireMaximumX ? position.X > corner.X : position.X < corner.X)
                return;
            if (requireMaximumY ? position.Y > corner.Y : position.Y < corner.Y)
                return;

            var normal = position - corner;
            if (normal.LengthSquared() <= float.Epsilon)
                return;
            normal = Vector2.Normalize(normal);
            if (Vector2.Dot(delta, normal) >= -ContactApproachEpsilon)
                return;

            bestHit = new SweepHit(time, normal);
            found = true;
        }
    }

    private bool IsMovementBlockedFromCache(int worldTileX, int worldTileY)
    {
        if (TryGetIndoorTile(worldTileX, worldTileY, out var indoorGroup, out var indoorX, out var indoorY))
            return indoorGroup.Pathing.IsBlocked(indoorX, indoorY);

        return !TryGetSectorTile(worldTileX, worldTileY, out var sector, out var localX, out var localY) ||
               sector.Pathing.IsBlocked(localX, localY);
    }

    private bool TryGetIndoorTile(
        int worldTileX,
        int worldTileY,
        out IndoorTileGroup group,
        out int localX,
        out int localY)
    {
        if (activeIndoorGroup() is { } active)
        {
            if (active.TryGetAuthoredLocalTile(worldTileX, worldTileY, out localX, out localY))
            {
                group = active;
                return true;
            }

            group = null!;
            localX = 0;
            localY = 0;
            return false;
        }

        foreach (var candidate in _indoorGroups)
        {
            if (candidate.SurfaceLevel != 1)
                continue;
            if (!candidate.TryGetAuthoredLocalTile(worldTileX, worldTileY, out localX, out localY))
                continue;

            group = candidate;
            return true;
        }

        group = null!;
        localX = 0;
        localY = 0;
        return false;
    }

    private bool TryGetSectorTile(
        int worldTileX,
        int worldTileY,
        out Sector sector,
        out int localX,
        out int localY)
    {
        var sectorCoord = new SectorCoord(
            FloorDiv(worldTileX, Sector.TileCount),
            FloorDiv(worldTileY, Sector.TileCount));
        localX = worldTileX - sectorCoord.X * Sector.TileCount;
        localY = worldTileY - sectorCoord.Y * Sector.TileCount;
        return _sectors.TryGetValue(sectorCoord, out sector!);
    }

    private void RefreshSectorIndex()
    {
        var visibleWorld = worldStreamer.VisibleWorld;
        if (ReferenceEquals(visibleWorld, _cachedWorld))
            return;

        _cachedWorld = visibleWorld;
        _sectors.Clear();
        _indoorGroups.Clear();
        var indoorGroupIds = new HashSet<IndoorTileGroupId>();
        foreach (var sector in visibleWorld.Sectors)
        {
            _sectors[sector.Coord] = sector;
            foreach (var group in sector.IndoorTileGroups.Groups)
                if (indoorGroupIds.Add(group.Id))
                    _indoorGroups.Add(group);
        }
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }

    private readonly record struct SweepHit(float Time, Vector2 Normal);
}
