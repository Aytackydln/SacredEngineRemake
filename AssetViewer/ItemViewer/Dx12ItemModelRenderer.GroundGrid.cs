using System;
using System.Numerics;

namespace AssetViewer.ItemViewer;

internal sealed partial class Dx12ItemModelRenderer
{
    private const float GroundGridCellWorldSize = 32.0f;
    private PreviewGroundGrid? _groundGrid;
    private bool _groundGridEnabled;

    public void SetGroundGridEnabled(bool enabled)
    {
        _groundGridEnabled = enabled;
        if (enabled)
            _groundGrid ??= new PreviewGroundGrid(_device, BackBufferFormat, DepthBufferFormat);
    }

    private void RecordGroundGrid()
    {
        if (!_groundGridEnabled || !_assetPreview || (_mesh is null && _fxPreview is null))
            return;

        var center = _mesh is null ? Vector3.Zero : (_meshBounds.Min + _meshBounds.Max) * 0.5f;
        var diameter = _fxPreview is null
            ? Math.Max(1, Vector3.Distance(_meshBounds.Min, _meshBounds.Max))
            : AssetPreviewDiameter();
        // GRN models are Z-up. Effects without a model use the emission origin's
        // ground, rather than following the moving particles' lowest extent.
        var floor = _mesh is null ? 0 : _meshBounds.Min.Z;
        var world = Matrix4x4.CreateTranslation(center.X, center.Y, floor - diameter * 0.002f) *
            CreateAssetWorldMatrix();
        // The lattice has a fixed world scale across previews. Only projection
        // changes its apparent spacing when the camera zooms.
        _groundGrid!.Record(_commandList, world * CreateViewProjectionMatrix(),
            GroundGridCellWorldSize, diameter * 1.5f);
    }
}
