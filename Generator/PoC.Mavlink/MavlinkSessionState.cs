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
        while (true)
        {
            int current = Volatile.Read(ref _version);
            var currentVersion = (MavlinkSessionVersion)current;

            MavlinkSessionVersion next;

            if (currentVersion == MavlinkSessionVersion.Unknown)
            {
                next = packetVersion == MavlinkPacketVersion.V2
                    ? MavlinkSessionVersion.V2
                    : MavlinkSessionVersion.V1;
            }
            else if ((currentVersion == MavlinkSessionVersion.V1 && packetVersion == MavlinkPacketVersion.V2) ||
                     (currentVersion == MavlinkSessionVersion.V2 && packetVersion == MavlinkPacketVersion.V1))
            {
                next = MavlinkSessionVersion.Hybrid;
            }
            else
            {
                break;
            }

            if (Interlocked.CompareExchange(ref _version, (int)next, current) == current)
            {
                VersionChanged?.Invoke(next);
                break;
            }
        }

        if (signed)
        {
            Interlocked.Exchange(ref _signed, 1);
        }
    }
}
