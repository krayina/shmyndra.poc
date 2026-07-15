using System.Runtime.CompilerServices;

namespace Mavlink;

#if NET8_0_OR_GREATER
[InlineArray(255)]
internal struct MavlinkPayloadBuffer
{
    internal byte Element0;
}
#else
internal unsafe struct MavlinkPayloadBuffer
{
    private fixed byte _data[255];

    public ReadOnlySpan<byte> AsSpan(int length)
    {
        fixed (byte* ptr = _data)
        {
            return new ReadOnlySpan<byte>(ptr, length);
        }
    }

    public void CopyFrom(ReadOnlySpan<byte> source)
    {
        fixed (byte* ptr = _data)
        {
            var target = new Span<byte>(ptr, 255);
            source.CopyTo(target);
        }
    }
}
#endif
