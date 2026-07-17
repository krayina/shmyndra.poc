namespace Mavlink.Common;

//[MavlinkGenerated(dialect: "common", xmlHash: "a1b2c3...")]
public readonly record struct HeartbeatMavlinkMessage : IMavlinkMessage
{
	public MavType Type { get; init; }
	public MavAutopilot Autopilot { get; init; }
	public MavModeFlag BaseMode { get; init; }
	public uint CustomMode { get; init; }
	public MavState SystemStatus { get; init; }
	public byte MavlinkVersion { get; init; }
}
