namespace Mavlink.Common;

public enum MavState : byte
{
	Uninit = 0,
	Boot = 1,
	Calibrating = 2,
	Standby = 3,
	Active = 4,
	Critical = 5,
	Emergency = 6,
	Poweroff = 7,
	FlightTermination = 8,
}
