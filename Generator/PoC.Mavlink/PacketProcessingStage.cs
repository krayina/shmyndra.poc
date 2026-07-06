using System.Runtime.CompilerServices;

namespace Mavlink;

internal sealed class PacketProcessingStage : IMavlinkPacketListener, IMavlinkParserErrorListener
{
    private readonly MavlinkDispatcher _dispatcher;
    private readonly MavlinkDiagnostics _diagnostics;
    private readonly Action? _onActivity;

    public PacketProcessingStage(
        MavlinkDispatcher dispatcher,
        MavlinkDiagnostics diagnostics,
        Action? onActivity = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _onActivity = onActivity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnPacketReceived(in MavlinkReceivedPacket packet)
    {
        _onActivity?.Invoke();
        _diagnostics.OnReceived();
        _diagnostics.TrackSequence(packet.SenderSystemId, packet.SenderComponentId, packet.Sequence);
        _dispatcher.TryEnqueue(in packet);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnParserError(MavlinkDeserializeResult result)
    {
        _diagnostics.OnDeserializeError(result);
    }
}
