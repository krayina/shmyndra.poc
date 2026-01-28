using System.Security.Cryptography;

namespace Mavlink;

/// <summary>
/// Represents a MAVLink v2 signing session.
/// Keeps track of the secret key, link ID, and the last used timestamp to prevent replay attacks.
/// <para>
/// You should reuse the same instance of this class across reconnections 
/// to maintain the correct timestamp sequence.
/// </para>
/// </summary>
public sealed class MavlinkSigner
{
    private readonly byte[] _secretKey;
    private ulong _lastTimestamp;

    /// <summary>
    /// Gets the Link ID used for this signing session.
    /// </summary>
    public byte LinkId { get; }

    /// <summary>
    /// Gets the last timestamp (in 10us units since 2015) used for signing a packet.
    /// </summary>
    public ulong LastTimestamp => _lastTimestamp;

    /// <summary>
    /// Initializes a new signing session.
    /// </summary>
    /// <param name="secretKey">The 32-byte SHA-256 secret key.</param>
    /// <param name="linkId">The Link ID (default 1). Use different IDs for different ground stations.</param>
    public MavlinkSigner(byte[] secretKey, byte linkId = 1)
    {
        if (secretKey == null || secretKey.Length != 32)
        {
            throw new ArgumentException("Key must be 32 bytes", nameof(secretKey));
        }
        _secretKey = secretKey;
        LinkId = linkId;
        _lastTimestamp = 0;
    }

    internal ulong GetNextTimestamp()
    {
        ulong now = (ulong)(DateTime.UtcNow - new DateTime(2015, 1, 1)).TotalMilliseconds * 100;

        if (now <= _lastTimestamp)
        {
            now = _lastTimestamp + 1;
        }

        _lastTimestamp = now;
        return now;
    }

    internal void ComputeSignature(ReadOnlySpan<byte> packetWithoutSignature, ulong timestamp, Span<byte> output48bit)
    {
        int totalLen = 32 + packetWithoutSignature.Length + 1 + 6;

        Span<byte> buffer = totalLen <= 512
            ? stackalloc byte[totalLen]
            : new byte[totalLen];

        _secretKey.CopyTo(buffer);
        packetWithoutSignature.CopyTo(buffer.Slice(32));
        buffer[32 + packetWithoutSignature.Length] = LinkId;
        MavlinkSigner.Store48BitTimestamp(timestamp, buffer.Slice(32 + packetWithoutSignature.Length + 1));

#if NET5_0_OR_GREATER
        Span<byte> sha256Output = stackalloc byte[32];
        SHA256.HashData(buffer, sha256Output);
        sha256Output.Slice(0, 6).CopyTo(output48bit);

#elif NETSTANDARD2_1_OR_GREATER
        using (var sha = SHA256.Create())
        {
            Span<byte> sha256Output = stackalloc byte[32];
            if (sha.TryComputeHash(buffer, sha256Output, out _))
            {
                sha256Output.Slice(0, 6).CopyTo(output48bit);
            }
        }

#else
        byte[] rentBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(totalLen);
        try
        {
            buffer.CopyTo(rentBuffer);
            using (var sha = SHA256.Create())
            {
                byte[] hashResult = sha.ComputeHash(rentBuffer, 0, totalLen);
                new ReadOnlySpan<byte>(hashResult, 0, 6).CopyTo(output48bit);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rentBuffer);
        }
#endif
    }

    public static void Store48BitTimestamp(ulong time, Span<byte> destination)
    {
        destination[0] = (byte)time;
        destination[1] = (byte)(time >> 8);
        destination[2] = (byte)(time >> 16);
        destination[3] = (byte)(time >> 24);
        destination[4] = (byte)(time >> 32);
        destination[5] = (byte)(time >> 40);
    }
}