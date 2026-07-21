namespace Mavlink;

internal sealed class MavlinkSignatureFrameVerifier : IMavlinkFrameVerifier
{
    private const int SignatureBlockLength = 13;

    private readonly MavlinkSignatureVerifier _verifier;
    private readonly MavlinkDiagnostics _diagnostics;

    public MavlinkSignatureFrameVerifier(
        MavlinkSignatureVerifier verifier, MavlinkDiagnostics diagnostics)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public bool Verify(ReadOnlySpan<byte> frame, in MavlinkReceivedPacket packet)
    {
        if (!packet.IsSigned)
        {
            if (_verifier.AllowUnsigned)
            {
                return true;
            }

            _diagnostics.OnSignatureFailure();
            return false;
        }

        if (frame.Length <= SignatureBlockLength)
        {
            _diagnostics.OnSignatureFailure();
            return false;
        }

        int splitAt = frame.Length - SignatureBlockLength;

        var result = _verifier.Verify(
            frame.Slice(0, splitAt),
            frame.Slice(splitAt, SignatureBlockLength),
            packet.SenderSystemId,
            packet.SenderComponentId);

        if (result == MavlinkSignatureVerifyResult.Valid)
        {
            return true;
        }

        _diagnostics.OnSignatureFailure();
        return false;
    }
}
