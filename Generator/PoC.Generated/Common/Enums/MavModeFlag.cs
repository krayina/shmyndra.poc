namespace Mavlink.Common;

[Flags]
public enum MavModeFlag : byte
{
	SafetyArmed = 128,
	ManualInputEnabled = 64,
	HilEnabled = 32,
	StabilizeEnabled = 16,
	GuidedEnabled = 8,
	AutoEnabled = 4,
	TestEnabled = 2,
	CustomModeEnabled = 1,
}
