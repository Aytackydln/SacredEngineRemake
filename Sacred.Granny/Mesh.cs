namespace Sacred.Granny;

/// <summary>
/// A mesh resource with stable reference identity. Vertex data may be updated in place by an
/// animation instance, so value-based record equality would make it unsafe to use as a GPU-cache key.
/// </summary>
public sealed class Mesh
{
    public Mesh(VertexPositionNormalTexture[] vertices, ushort[] indices)
    {
        Vertices = vertices;
        Indices = indices;
    }

    public VertexPositionNormalTexture[] Vertices { get; }

    public ushort[] Indices { get; }

    public IReadOnlyList<MeshSurface> Surfaces { get; init; } = [];

    /// <summary>
    /// Changes whenever the CPU-side vertex buffer is modified. Renderers use this to refresh
    /// an existing GPU buffer without recreating the mesh and its material bindings.
    /// </summary>
    public ulong VertexRevision { get; private set; }

    public Mesh CreateInstance() => new((VertexPositionNormalTexture[])Vertices.Clone(), (ushort[])Indices.Clone())
    {
        Surfaces = Surfaces
    };

    internal void MarkVerticesChanged() => VertexRevision++;

    /// <summary>Notifies renderers after a caller updates the mutable vertex array in place.</summary>
    public void NotifyVerticesChanged() => MarkVerticesChanged();
}
