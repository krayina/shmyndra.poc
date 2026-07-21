using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Mavlink;

public sealed class MavlinkSignatureVerifier : IDisposable
{
    private const long Ticks2015 = 635556672000000000;

    private readonly byte[] _secretKey;
    private readonly ConcurrentDictionary<int, ulong> _streamTimestamps = new();

#if !NET6_0_OR_GREATER
    private readonly SHA256 _hasher = SHA256.Create();
    private readonly object _hasherLock = new();
#endif

    public bool AllowUnsigned { get; init; } = true;

    public TimeSpan NewStreamTolerance { get; init; } = TimeSpan.FromMinutes(1);

    public MavlinkSignatureVerifier(byte[] secretKey)
    {
        if (secretKey == null || secretKey.Length != 32)
        {
            throw new ArgumentException("Key must be 32 bytes", nameof(secretKey));
        }

        _secretKey = new byte[32];
        secretKey.AsSpan().CopyTo(_secretKey);
    }

    public MavlinkSignatureVerifyResult Verify(
        ReadOnlySpan<byte> packetWithCrc,
        ReadOnlySpan<byte> signatureBlock,
        byte senderSystemId,
        byte senderComponentId)
    {
        byte linkId = signatureBlock[0];
        ulong timestamp = Read48BitTimestamp(signatureBlock.Slice(1));
        int streamKey = (linkId << 16) | (senderSystemId << 8) | senderComponentId;

        if (_streamTimestamps.TryGetValue(streamKey, out var seen) && timestamp <= seen)
        {
            return MavlinkSignatureVerifyResult.TimestampReplay;
        }

        Span<byte> expected = stackalloc byte[6];
        ComputeSignature(packetWithCrc, linkId, timestamp, expected);

        if (!FixedTimeEquals(expected, signatureBlock.Slice(7, 6)))
        {
            return MavlinkSignatureVerifyResult.BadSignature;
        }

        while (true)
        {
            if (_streamTimestamps.TryGetValue(streamKey, out var last))
            {
                if (timestamp <= last)
                {
                    return MavlinkSignatureVerifyResult.TimestampReplay;
                }

                if (_streamTimestamps.TryUpdate(streamKey, timestamp, last))
                {
                    return MavlinkSignatureVerifyResult.Valid;
                }
                continue;
            }

            if (NewStreamTolerance != TimeSpan.MaxValue)
            {
                ulong now = (ulong)((DateTime.UtcNow.Ticks - Ticks2015) / 100);
                ulong tolerance = (ulong)(NewStreamTolerance.Ticks / 100);
                ulong diff = now > timestamp ? now - timestamp : timestamp - now;

                if (diff > tolerance)
                {
                    return MavlinkSignatureVerifyResult.NewStreamTimestampOutOfRange;
                }
            }

            if (_streamTimestamps.TryAdd(streamKey, timestamp))
            {
                return MavlinkSignatureVerifyResult.Valid;
            }
        }
    }

    private void ComputeSignature(
        ReadOnlySpan<byte> packetWithCrc, byte linkId, ulong timestamp, Span<byte> output48bit)
    {
        int totalLen = 32 + packetWithCrc.Length + 1 + 6;

        Span<byte> buffer = totalLen <= 512
            ? stackalloc byte[totalLen]
            : new byte[totalLen];

        _secretKey.CopyTo(buffer);
        packetWithCrc.CopyTo(buffer.Slice(32));
        buffer[32 + packetWithCrc.Length] = linkId;
        Store48BitTimestamp(timestamp, buffer.Slice(32 + packetWithCrc.Length + 1));

#if NET6_0_OR_GREATER
        Span<byte> sha = stackalloc byte[32];
        SHA256.HashData(buffer, sha);
        sha.Slice(0, 6).CopyTo(output48bit);
#else
        lock (_hasherLock)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
            Span<byte> sha = stackalloc byte[32];
            if (_hasher.TryComputeHash(buffer, sha, out _))
            {
                sha.Slice(0, 6).CopyTo(output48bit);
            }
#else
            byte[] rent = ArrayPool<byte>.Shared.Rent(totalLen);
            try
            {
                buffer.CopyTo(rent);
                byte[] hash = _hasher.ComputeHash(rent, 0, totalLen);
                new ReadOnlySpan<byte>(hash, 0, 6).CopyTo(output48bit);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rent);
            }
#endif
        }
#endif
    }

    private static bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
        return CryptographicOperations.FixedTimeEquals(a, b);
#else
        if (a.Length != b.Length)
        {
            return false;
        }
 
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
 
        return diff == 0;
#endif
    }

    private static ulong Read48BitTimestamp(ReadOnlySpan<byte> src)
    {
        return src[0]
           | ((ulong)src[1] << 8)
           | ((ulong)src[2] << 16)
           | ((ulong)src[3] << 24)
           | ((ulong)src[4] << 32)
           | ((ulong)src[5] << 40);
    }

    private static void Store48BitTimestamp(ulong time, Span<byte> destination)
    {
        destination[0] = (byte)time;
        destination[1] = (byte)(time >> 8);
        destination[2] = (byte)(time >> 16);
        destination[3] = (byte)(time >> 24);
        destination[4] = (byte)(time >> 32);
        destination[5] = (byte)(time >> 40);
    }

    public void Dispose()
    {
#if !NET6_0_OR_GREATER
        _hasher?.Dispose();
#endif
        CryptographicZero(_secretKey);
    }

    private static void CryptographicZero(byte[] key)
    {
#if NETCOREAPP3_0_OR_GREATER
        CryptographicOperations.ZeroMemory(key);
#else
        Array.Clear(key, 0, key.Length);
#endif
    }
}
