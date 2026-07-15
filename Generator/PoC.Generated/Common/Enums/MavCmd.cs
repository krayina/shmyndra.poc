namespace Mavlink.Common;

public enum MavCmd : ushort
{
    NavWaypoint = 16,
    NavReturnToLaunch = 20,
    NavTakeoff = 22,
    ComponentArmDisarm = 400,
    RequestMessage = 512,
}
