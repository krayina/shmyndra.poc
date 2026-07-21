using Mavlink.Transport;

namespace Mavlink;

public sealed class TlogFrameRecorder : IMavlinkRawFrameListener
{
    private readonly TlogWriter _writer;

    public TlogFrameRecorder(TlogWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void OnFrame(MavlinkFrameDirection direction, ReadOnlySpan<byte> frame)
    {
        _writer.Append(frame);
    }
}
