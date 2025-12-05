using System.Buffers;

namespace Mavlink.Common;

//public static class CommonSender
//{
//    public static ValueTask SendGeneratedAsync(this MavlinkClient client, IMavlinkMessage message, CancellationToken ct)
//    {
//        switch (message)
//        {
//            case HeartbeatMavlinkMessage msg:
//                return client.SendAsync(msg, ct);
//            default:
//                throw new ArgumentException($"Unknown message type: {message.GetType().Name}");
//        }
//    }
//}

//public static class CommonSender
//{
//	public static ValueTask SendAsync(this MavlinkClient client, IMavlinkMessage message, CancellationToken ct = default)
//	{
//		switch (message)
//		{
//			case HeartbeatMavlinkMessage msg:
//				return client.SendAsync(msg, ct);
//			default:
//				throw new ArgumentException($"Message type {message.GetType().Name} is not supported by the currently linked dialects.");
//		}
//	}

	//public static ValueTask SendAsync(this MavlinkClient client, HeartbeatMavlinkMessage message, CancellationToken ct = default)
	//{
	//	byte[] buffer = ArrayPool<byte>.Shared.Rent(280);
	//	try
	//	{
	//		int length = SerializePacket<HeartbeatMavlinkMessage, HeartbeatMavlinkMessageProvider>(message, buffer);

	//		return client.SendAsync(buffer.AsMemory(0, length), ct);
	//	}
	//	finally
	//	{
	//		ArrayPool<byte>.Shared.Return(buffer);
	//	}
	//}

//	public static ValueTask SendAsync(
//		this MavlinkClient client,
//		HeartbeatMavlinkMessage message,
//		CancellationToken ct = default)
//	{
//		return client.SerializeAndSend<HeartbeatMavlinkMessage, HeartbeatMavlinkMessageProvider>(message, ct);
//	}


//	//	// Не повинно бути згенеровано
//	//	private static int SerializePacket<TMessage, TProvider>(
//	//		TMessage message,
//	//		Span<byte> destination)
//	//		where TMessage : MavlinkMessage
//	//		where TProvider : IMessageInfoProvider<TMessage>
//	//	{
//	//		destination[0] = 0xFD; // MAVLink 2.0 STX
//	//		destination[1] = (byte)TProvider.PayloadLength;
//	//		destination[2] = 0; // Sequence (клієнт має керувати цим)
//	//		destination[3] = 1; // System ID (з налаштувань клієнта)
//	//		destination[4] = 1; // Component ID (з налаштувань клієнта)

//	//		uint msgId = TProvider.MessageId;
//	//		destination[5] = (byte)msgId;
//	//		destination[6] = (byte)(msgId >> 8);
//	//		destination[7] = (byte)(msgId >> 16);

//	//		const int headerLength = 8;

//	//		var payloadSpan = destination.Slice(headerLength, TProvider.PayloadLength);
//	//		TProvider.SerializePayload(message, payloadSpan);

//	//		int packetLengthWithoutCrc = headerLength + TProvider.PayloadLength;
//	//		var crc = CalculateCrc(destination.Slice(1, packetLengthWithoutCrc - 1), TProvider.CrcExtra);

//	//		destination[packetLengthWithoutCrc] = (byte)crc;
//	//		destination[packetLengthWithoutCrc + 1] = (byte)(crc >> 8);

//	//		return packetLengthWithoutCrc + 2;
//	//	}
//}
