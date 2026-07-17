namespace Mavlink.Ardupilotmega;

public enum MavCmd : uint
{
    NavWaypoint = (uint)Common.MavCmd.NavWaypoint,
    NavReturnToLaunch = (uint)Common.MavCmd.NavReturnToLaunch,
    NavTakeoff = (uint)Common.MavCmd.NavTakeoff,
    ComponentArmDisarm = (uint)Common.MavCmd.ComponentArmDisarm,
    RequestMessage = (uint)Common.MavCmd.RequestMessage,
    DoParachute = 208
}
