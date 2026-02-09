namespace Mavlink;

public sealed class MavlinkSessionState
{
    private int _sequence;
    private int _version;
    private int _signed;

    public MavlinkSessionVersion Version
        => (MavlinkSessionVersion)Volatile.Read(ref _version);

    public bool IsSigned
        => Volatile.Read(ref _signed) != 0;

    public event Action<MavlinkSessionVersion>? VersionChanged;

    public byte NextSequence()
        => (byte)Interlocked.Increment(ref _sequence);

    internal void UpdateFromPacket(MavlinkPacketVersion packetVersion, bool signed)
    {
        var current = (MavlinkSessionVersion)Volatile.Read(ref _version);
        var next = current;

        if (current == MavlinkSessionVersion.Unknown)
        {
            next = packetVersion == MavlinkPacketVersion.V2
                ? MavlinkSessionVersion.V2
                : MavlinkSessionVersion.V1;
        }
        else if ((current == MavlinkSessionVersion.V1 && packetVersion == MavlinkPacketVersion.V2) ||
                 (current == MavlinkSessionVersion.V2 && packetVersion == MavlinkPacketVersion.V1))
        {
            next = MavlinkSessionVersion.Hybrid;
        }

        if (next != current)
        {
            Interlocked.Exchange(ref _version, (int)next);
            VersionChanged?.Invoke(next);
        }

        if (signed)
        {
            Interlocked.Exchange(ref _signed, 1);
        }
    }
}
