namespace Mavlink;

[Flags]
public enum MavlinkIncompatFlags : byte
{
	None = 0,
	Signed = 0x01
}

[Flags]
public enum MavlinkCompatFlags : byte
{
	None = 0
}

internal static class MavlinkConstants
{
	// Magic bytes
	public const byte MAGIC_V1 = 0xFE;
	public const byte MAGIC_V2 = 0xFD;

	// Header sizes
	public const int HEADER_V1_LENGTH = 6;
	public const int HEADER_V2_LENGTH = 10;

	// Offsets V1
	public const int V1_PAYLOAD_LENGTH_OFFSET = 1;
	public const int V1_SEQUENCE_OFFSET = 2;
	public const int V1_SYSID_OFFSET = 3;
	public const int V1_COMPID_OFFSET = 4;
	public const int V1_MSGID_OFFSET = 5;

	// Offsets V2
	public const int V2_PAYLOAD_LENGTH_OFFSET = 1;
	public const int V2_INCOMPAT_FLAGS_OFFSET = 2;
	public const int V2_COMPAT_FLAGS_OFFSET = 3;
	public const int V2_SEQUENCE_OFFSET = 4;
	public const int V2_SYSID_OFFSET = 5;
	public const int V2_COMPID_OFFSET = 6;
	public const int V2_MSGID_OFFSET = 7; // 3 bytes: 7,8,9

	public const int CRC_LENGTH = 2;
	public const int SIGNATURE_LENGTH = 13;

	public const int MAX_PAYLOAD_V1 = 255;
	public const int MAX_PAYLOAD_V2 = 255;

	public const int MAX_PAYLOAD_ARRAY_POOL_SIZE = 512;
}
