using Mavlink.Routing;
using System.Runtime.CompilerServices;

namespace Mavlink;

internal sealed class PacketProcessingStage : IMavlinkPacketListener, IMavlinkParserErrorListener
{
    private readonly MavlinkDispatcher _dispatcher;
    private readonly MavlinkDiagnostics _diagnostics;
    private readonly MavlinkNodeRegistry? _registry;
    private readonly Action? _onActivity;

    public PacketProcessingStage(
        MavlinkDispatcher dispatcher,
        MavlinkDiagnostics diagnostics,
        MavlinkNodeRegistry? registry = null,
        Action? onActivity = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _registry = registry;
        _onActivity = onActivity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnPacketReceived(in MavlinkReceivedPacket packet)
    {
        _onActivity?.Invoke();
        _registry?.OnPacket(in packet);
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
