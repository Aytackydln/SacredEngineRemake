using System;
using System.Collections.Generic;
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
        if (batch.Submissions is { } spriteSubmissions && batch.StaticRanges is { } ranges)
        {
            var rangeIndex = 0;
            foreach (var submission in spriteSubmissions)
            {
                while (rangeIndex < ranges.Count && submission.Instance >=
                       ranges[rangeIndex].StartInstance + ranges[rangeIndex].InstanceCount)
                    rangeIndex++;
                if (rangeIndex == ranges.Count) break;
                var range = ranges[rangeIndex];
                if (range.IsPostModel) continue;
                var sprite = submission.Sprite;
                var queue = sprite.IsParticleSprite ? (int)WorldRenderLayer.Objects : sprite.QueueIndex;
                if (queue < (int)firstLayer || queue > (int)lastLayer) continue;
                _submissions.Add(new Submission(queue,
                    sprite.ParticleDepthKey ?? sprite.TileDepth, sprite.TileWorldX,
                    sprite.ChainDepth, sprite.InsertionOrder, null,
                    range with { StartInstance = submission.Instance, InstanceCount = 1 }));
            }
        }
        for (var index = 0; firstLayer <= WorldRenderLayer.Objects && lastLayer >= WorldRenderLayer.Objects &&
             index < scene.Models.Count; index++)
        {
            var model = scene.Models[index];
            var anchor = model.DepthAnchor;
            // Native drawLine finishes the static chain before enqueueing the
            // runtime objects belonging to that same tile.
            var x = (int)MathF.Floor(anchor.X);
            var y = (int)MathF.Floor(anchor.Y);
            _submissions.Add(new Submission(3, x + y, x, int.MaxValue, index, model, default));
        }
        _submissions.Sort(static (left, right) =>
        {
            var order = left.Queue.CompareTo(right.Queue);
            if (order == 0) order = left.Diagonal.CompareTo(right.Diagonal);
            if (order == 0) order = left.X.CompareTo(right.X);
            if (order == 0) order = left.Chain.CompareTo(right.Chain);
            return order == 0 ? left.Insertion.CompareTo(right.Insertion) : order;
        });
        foreach (var submission in _submissions)
        {
            if (submission.Model is { } model)
            {
                Flush();
                _oneModel[0] = model;
                models.Record(camera, _oneModel, scene.Lighting, display, frame.Index);
                continue;
            }
            var next = submission.Range;
            if (_ranges.Count > 0 && _ranges[^1] is var previous &&
                previous.StartInstance + previous.InstanceCount == next.StartInstance &&
                previous with { StartInstance = next.StartInstance, InstanceCount = 1 } == next)
                _ranges[^1] = previous with { InstanceCount = previous.InstanceCount + 1 };
            else
                _ranges.Add(next);
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

    private readonly record struct Submission(int Queue, float Diagonal, int X, int Chain,
        int Insertion, SceneModel? Model, StaticSpriteDrawRange Range);
}
