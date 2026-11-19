using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sacred.Core.World;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Models;
using Sacred.Engine.Graphics.Sprites;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;

namespace Sacred.Engine.Graphics;

/// <summary>Interleaves sprite batches and models in Sacred's Objects queue.</summary>
internal sealed class Dx12WorldPainterPass(Dx12SpritePass sprites, Dx12ModelPass models)
{
    private readonly List<Submission> _submissions = new();
    private readonly List<StaticSpriteDrawRange> _ranges = new();
    private readonly SceneModel[] _oneModel = new SceneModel[1];
    // Sort keys are kept apart from the payload: ordering 24-byte keys with an inlined comparer moves far less
    // memory than ordering the full submissions through a delegate.
    private SortKey[] _keys = new SortKey[2048];
    public Action<uint>? BeforeSprite { get; set; }
    public Func<uint, bool>? HasSpriteEffect { get; set; }

    public void RecordFront(WorldSpriteBatch batch, SceneState scene, Dx12SceneColorProfile display,
        Dx12FrameContext frame, int width, int height)
    {
        if (batch.Submissions is null || batch.StaticRanges is null) return;
        var submissions = ReadOnlyListSpan.Of(batch.Submissions);
        var ranges = ReadOnlyListSpan.Of(batch.StaticRanges);
        _ranges.Clear();
        var rangeIndex = 0;
        foreach (var submission in submissions)
        {
            while (rangeIndex < ranges.Length && submission.Instance >=
                   ranges[rangeIndex].StartInstance + ranges[rangeIndex].InstanceCount) rangeIndex++;
            if (rangeIndex == ranges.Length) break;
            var range = ranges[rangeIndex];
            if (!range.IsPostModel) continue;
            if (submission.Sprite.HasLightHalo && HasSpriteEffect?.Invoke(submission.Sprite.StaticObjectId) == true)
            {
                Flush();
                BeforeSprite?.Invoke(submission.Sprite.StaticObjectId);
            }
            AppendRange(range with { StartInstance = submission.Instance, InstanceCount = 1 });
        }
        Flush();

        void Flush()
        {
            if (_ranges.Count == 0) return;
            sprites.RecordTransparentStatic(batch with { StaticRanges = _ranges },
                scene.Lighting.WorldSurfaceAmbientColour, display.SceneWhiteScale,
                display.UnlitSpriteScale, frame, width, height);
            _ranges.Clear();
        }
    }

    public void Record(WorldSpriteBatch batch, SacredCamera camera, SceneState scene,
        Dx12SceneColorProfile display, Dx12FrameContext frame, int width, int height)
        => RecordLayers(batch, camera, scene, display, frame, width, height,
            WorldRenderLayer.Floor2, WorldRenderLayer.Ceiling);

    public void RecordFloor(WorldSpriteBatch batch, SacredCamera camera, SceneState scene,
        Dx12SceneColorProfile display, Dx12FrameContext frame, int width, int height)
        => RecordLayers(batch, camera, scene, display, frame, width, height,
            WorldRenderLayer.Floor, WorldRenderLayer.Floor);

    private void RecordLayers(WorldSpriteBatch batch, SacredCamera camera, SceneState scene,
        Dx12SceneColorProfile display, Dx12FrameContext frame, int width, int height,
        WorldRenderLayer firstLayer, WorldRenderLayer lastLayer)
    {
        _submissions.Clear();
        _ranges.Clear();
        if (batch.Submissions is not null && batch.StaticRanges is not null)
        {
            var spriteSubmissions = ReadOnlyListSpan.Of(batch.Submissions);
            var ranges = ReadOnlyListSpan.Of(batch.StaticRanges);
            var rangeIndex = 0;
            foreach (var submission in spriteSubmissions)
            {
                while (rangeIndex < ranges.Length && submission.Instance >=
                       ranges[rangeIndex].StartInstance + ranges[rangeIndex].InstanceCount)
                    rangeIndex++;
                if (rangeIndex == ranges.Length) break;
                var range = ranges[rangeIndex];
                if (range.IsPostModel) continue;
                var sprite = submission.Sprite;
                var queue = sprite.IsParticleSprite ? (int)WorldRenderLayer.Objects : sprite.QueueIndex;
                if (queue < (int)firstLayer || queue > (int)lastLayer) continue;
                AddSubmission(queue,
                    // Native renderObjects emits its entire temporary-animation
                    // patch list before walking the Objects queue.
                    sprite.IsGroundDecal ? float.NegativeInfinity : sprite.ParticleDepthKey ?? sprite.TileDepth, sprite.TileWorldX,
                    sprite.ChainDepth, sprite.InsertionOrder,
                    new Submission(null, range with { StartInstance = submission.Instance, InstanceCount = 1 },
                        sprite.HasLightHalo ? sprite.StaticObjectId : null));
            }
        }
        var sceneModels = scene.Models;
        var modelCount = firstLayer <= WorldRenderLayer.Objects && lastLayer >= WorldRenderLayer.Objects
            ? sceneModels.Count : 0;
        for (var index = 0; index < modelCount; index++)
        {
            var model = sceneModels[index];
            var anchor = model.DepthAnchor;
            // Native drawLine finishes the static chain before enqueueing the
            // runtime objects belonging to that same tile.
            var x = (int)MathF.Floor(anchor.X);
            var y = (int)MathF.Floor(anchor.Y);
            AddSubmission(3, x + y, x, int.MaxValue, index, new Submission(model, default, null));
        }
        var count = _submissions.Count;
        var keys = _keys;
        new Span<SortKey>(keys, 0, count).Sort(new SortKeyOrder());
        var payloads = CollectionsMarshal.AsSpan(_submissions);
        for (var position = 0; position < count; position++)
        {
            ref readonly var submission = ref payloads[keys[position].Index];
            if (submission.Model is { } model)
            {
                Flush();
                _oneModel[0] = model;
                models.Record(camera, _oneModel, scene.Lighting, display, frame.Index);
                continue;
            }
            var next = submission.Range;
            if (submission.SourceId is { } source && HasSpriteEffect?.Invoke(source) == true)
            {
                Flush();
                BeforeSprite?.Invoke(source);
            }
            AppendRange(next);
        }
        Flush();

        void Flush()
        {
            if (_ranges.Count == 0) return;
            sprites.RecordOpaqueStatic(batch with { StaticRanges = _ranges },
                scene.Lighting.WorldSurfaceAmbientColour, display.SceneWhiteScale,
                display.UnlitSpriteScale, frame, width, height);
            _ranges.Clear();
        }
    }

    private void AddSubmission(int queue, float diagonal, int x, int chain, int insertion, in Submission submission)
    {
        var index = _submissions.Count;
        _submissions.Add(submission);
        if (index == _keys.Length) Array.Resize(ref _keys, _keys.Length * 2);
        _keys[index] = new SortKey(queue, diagonal, x, chain, insertion, index);
    }

    private void AppendRange(StaticSpriteDrawRange next)
    {
        if (_ranges.Count > 0 && _ranges[^1] is var previous &&
            previous.StartInstance + previous.InstanceCount == next.StartInstance &&
            previous with { StartInstance = next.StartInstance, InstanceCount = 1 } == next)
            _ranges[^1] = previous with { InstanceCount = previous.InstanceCount + 1 };
        else _ranges.Add(next);
    }

    private readonly record struct Submission(SceneModel? Model, StaticSpriteDrawRange Range, uint? SourceId);

    private readonly record struct SortKey(int Queue, float Diagonal, int X, int Chain, int Insertion, int Index);

    /// <summary>Same ordering as before (queue, diagonal, x, chain, insertion); the original index only breaks exact ties,
    /// which makes the order deterministic where the previous unstable sort left it unspecified.</summary>
    private readonly struct SortKeyOrder : IComparer<SortKey>
    {
        public int Compare(SortKey left, SortKey right)
        {
            var order = left.Queue.CompareTo(right.Queue);
            if (order == 0) order = left.Diagonal.CompareTo(right.Diagonal);
            if (order == 0) order = left.X.CompareTo(right.X);
            if (order == 0) order = left.Chain.CompareTo(right.Chain);
            if (order == 0) order = left.Insertion.CompareTo(right.Insertion);
            return order != 0 ? order : left.Index.CompareTo(right.Index);
        }
    }
}
