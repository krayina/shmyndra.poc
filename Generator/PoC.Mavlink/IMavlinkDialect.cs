namespace Mavlink.Dialects;

public interface IMavlinkDialect
{
    string Name { get; }
    IMavlinkMessageInfo? GetInfo(uint msgId);
    IMavlinkMessageInfo? GetInfo(Type type);
}
