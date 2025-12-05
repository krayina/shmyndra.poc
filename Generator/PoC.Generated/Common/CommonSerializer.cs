//using System.Collections.Frozen;
//using MavlinkTypes;

//namespace Mavlink.Common;

//public static class CommonSerializer
//{
//	private readonly static FrozenDictionary<int, (Type Type, int Crc, int Length, PayloadSerialization.HeartbeatPayloadSerializer PayloadSerializer)> _metadataById
//		= new Dictionary<int, (Type Type, int Crc, int Length, PayloadSerialization.HeartbeatPayloadSerializer PayloadSerializer)>
//		{
//			{ 0, (typeof(HeartbeatMavlinkMessage), 50, 9, new PayloadSerialization.HeartbeatPayloadSerializer()) }
//		}.ToFrozenDictionary();

//	private readonly static FrozenDictionary<Type, (int Id, int Crc, int Length, PayloadSerialization.HeartbeatPayloadSerializer PayloadSerializer)> _metadataByType
//		= new Dictionary<Type, (int Id, int Crc, int Length, PayloadSerialization.HeartbeatPayloadSerializer PayloadSerializer)>
//		{
//			{ typeof(HeartbeatMavlinkMessage), (0, 50, 9, new PayloadSerialization.HeartbeatPayloadSerializer()) }
//		}.ToFrozenDictionary();

//	public static bool TrySerialize<T>(T mavlinkMessage, out byte[]? packet) where T : MavlinkMessage
//	{
//		var test = _metadataByType[typeof(T)];
//		packet = [];
//		return true;
//	}

//	public static bool TryDeserialize(byte[] packet, out MavlinkMessage? message)
//	{
//		message = null;
//		return true;
//	}

//	public static bool TryDeserialize(ReadOnlySpan<byte> packet, out MavlinkMessage? message)
//	{
//		message = null;
//		return true;
//	}

//	public static MavlinkMessage Deserialize(ReadOnlySpan<byte> packet)
//	{
//		throw new NotImplementedException();
//	}

//	public static MavlinkMessage Deserialize(byte[] packet)
//	{
//		throw new NotImplementedException();
//	}
//}
