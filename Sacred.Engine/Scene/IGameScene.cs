using System;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Engine.Graphics;

namespace Sacred.Engine.Scene;

internal interface IGameScene : IDisposable
{
    GameSceneId Id { get; }
    void OnActivated();
    void OnDeactivated();
    void Update(float deltaSeconds);
    ValueTask RenderAsync(SceneRenderContext context);
}

internal readonly record struct SceneRenderContext(
    Dx12Renderer Renderer,
    bool VerticalSyncEnabled,
    string FramePacingStatus,
    ulong FrameId,
    CancellationToken CancellationToken);
