using Mavlink;
using Mavlink.Common;
using Mavlink.Transport;

await using var client = await MavlinkClient.ConnectAsync("tcp://127.0.0.1:5760");

client.Subscribe<HeartbeatMavlinkMessage>((msg, packet) =>
    Console.WriteLine($"Heartbeat від sys={packet.SenderSystemId}"));


bool canConnect = MavlinkEndpoint.TryParse("tcp://127.0.0.1:5760", out _, out var error);


//await channel.ConnectAsync();

//var drone1 = channel.GetSystem(1);

//drone1.Subscribe<CargoSlotStatusMavlinkMessage>((msg, pkt) =>
//    UpdateSlotUi(slotId: pkt.SenderComponentId, msg));

//await gcs.SendToAsync(new CommandLongMavlinkMessage
//{
//    Command = MavCmd.DoCargoRelease /* умовно */,
//}, drone1);
//drone1.Subscribe<CommandAckMavlinkMessage>((ack, pkt) =>
//    Console.WriteLine(($"Слот {pkt.SenderComponentId}: {ack.Result}"));

//await gcs.SendToAsync(releaseCmd, drone1.GetComponent(26 /* слот №2 */));


Console.WriteLine("Hello, World!");