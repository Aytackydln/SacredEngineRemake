using Sacred.Network.Gameplay;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class DoorSceneController
{
    private GoldConsumerCursor? _presentationCursor;
    private GoldWorldReplica? _presentationWorld;
    public void ConsumeNetworkAnimations(GoldWorldReplica world)
    {
        if (!ReferenceEquals(_presentationWorld, world))
        {
            _presentationWorld = world;
            _presentationCursor = world.Consumers.CreateCursor("world-object-animations", w => w.Value is GoldObjectPresentationWork { Object.Creature: null });
        }
        while (world.Consumers.TryPeek(_presentationCursor!, out var work))
        {
            var command = (GoldObjectPresentationWork)work.Value;
            var reference = command.Object.BaseState.ObjectReference;
            var result = GoldConsumerOutcome.Rejected;
            string detail;
            if (!world.Objects.TryGetValue(reference, out var current) || current.InstanceVersion != command.Object.InstanceVersion)
                detail = "object retired or reference reused";
            else if (command.Object.Creature is not null)
                detail = "creature motion is not a world-prop activation clip";
            else if (command.Event is GoldContainerCloseEvent && command.Object.BaseState.TriggerReference != 0)
                detail = "linked trigger activation requires its gameplay callback consumer";
            else if (!_doorAnimations.TryGetValue(reference, out var playback))
            { result = GoldConsumerOutcome.Deferred; detail = "waiting for original world-prop model and activation clips"; }
            else
            {
                uint motion = command.Event is GoldObjectAnimationEvent animation ? animation.Animation :
                    current.Physical?.Word64 == 0xAA && playback.CanReset ? 0xABu : 0xAAu;
                if (command.Event is GoldObjectAnimationEvent arguments &&
                    (arguments.FirstArgumentBits != 0x3F800000 || arguments.SecondArgumentBits != 0x3F800000))
                    detail = "world-prop playback requires decoded rate/blend arguments (native activation uses 1,1)";
                else if (motion is not (0xAA or 0xAB)) detail = $"motion {motion} is not an implemented world-prop activation clip";
                else if (!(motion == 0xAA ? playback.CanActivate : playback.CanReset)) detail = $"original model has no motion {motion}";
                else if (!world.CompleteObjectAnimation(command, motion)) detail = "object no longer accepts animation";
                else
                {
                    playback.SetState(motion == 0xAA);
                    _animationStates[reference] = motion == 0xAA;
                    result = GoldConsumerOutcome.Consumed; detail = $"playing original motion {motion} on ref {reference}";
                    EngineLog.WriteLine($"Network object animation: {detail}.");
                }
            }
            world.Consumers.Report(_presentationCursor!, work, result, detail);
            if (result == GoldConsumerOutcome.Deferred) break;
        }
    }
}
