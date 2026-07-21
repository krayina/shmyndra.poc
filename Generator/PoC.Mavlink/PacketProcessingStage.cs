using Mavlink.Routing;

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
        MavlinkNodeRegistry? registry,
        Action? onActivity)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _registry = registry;
        _onActivity = onActivity;
    }

    public void OnPacketReceived(in MavlinkReceivedPacket packet)
    {
        _diagnostics.OnReceived(WireLength(in packet));
        _diagnostics.TrackSequence(
            packet.SenderSystemId, packet.SenderComponentId, packet.Sequence);

        _onActivity?.Invoke();
        _registry?.OnPacket(in packet);

        if (!_dispatcher.TryEnqueue(in packet))
        {
            _diagnostics.OnChannelOverflow();
        }
    }

    public void OnParserError(MavlinkDeserializeResult result)
    {
        _diagnostics.OnDeserializeError(result);
    }

    public void OnReceiverFault(Exception exception)
    {
        _diagnostics.OnReceiverFault(exception);
    }

    private static int WireLength(in MavlinkReceivedPacket packet)
    {
        int header = packet.Version == MavlinkPacketVersion.V2
            ? MavlinkConstants.HEADER_V2_LENGTH
            : MavlinkConstants.HEADER_V1_LENGTH;

        return header + packet.PayloadLength + 2 + (packet.IsSigned ? 13 : 0);
    }
}
