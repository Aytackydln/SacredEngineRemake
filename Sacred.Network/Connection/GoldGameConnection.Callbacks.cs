using System.Net.Sockets;
using System.Threading.Channels;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

namespace Sacred.Network.Connection;

public sealed partial class GoldGameConnection
{
    // Reading must continue while an atomic character upload holds the send gate.
    // A single worker retains probe order without concurrent callback sends.
    private readonly Channel<(GoldTimeProbeEvent Probe, byte Slot)> _timeProbes =
        Channel.CreateUnbounded<(GoldTimeProbeEvent, byte)>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private async Task ReplyToTimeProbesAsync()
    {
        try
        {
            await foreach (var (probe, slot) in _timeProbes.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                var reply = GoldTimeProbeCodec.CreateReply(probe, slot, unchecked((uint)_clock.ElapsedMilliseconds));
                await SendCoreAsync(reply, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        { StopForTransportFailure(error); }
        catch (InvalidOperationException) when (_stop.IsCancellationRequested || Admission.Phase == GoldAdmissionPhase.Rejected)
        {
            // The reader owns the native rejection and its final close reason.
        }
    }
}
