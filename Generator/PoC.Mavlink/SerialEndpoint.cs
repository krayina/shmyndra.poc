using System.IO.Ports;

namespace Mavlink.Transport;

/// <summary>
/// Serial (COM/tty) transport. String forms:
///   serial:COM3?baud=57600
///   serial:/dev/ttyUSB0:115200
///   COM3:57600            (bare shortcut)
///   /dev/ttyACM0          (bare shortcut, default baud)
/// Query: ?baud=, ?parity=none|odd|even|mark|space, ?databits=, ?stopbits=1|1.5|2, ?dtr=, ?rts=
///
/// When the cable is unplugged, reads fail, the channel closes the port and
/// the reconnect policy keeps retrying SerialPort.Open() until the device is
/// back — infinite waiting works out of the box with the default policy.
/// </summary>
public sealed class SerialEndpoint : MavlinkEndpoint
{
    public SerialEndpoint(string portName)
    {
        PortName = portName ?? throw new ArgumentNullException(nameof(portName));
    }

    public string PortName { get; }

    public int BaudRate { get; set; } = 57600;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public Handshake Handshake { get; set; } = Handshake.None;

    public bool DtrEnable { get; set; }

    public bool RtsEnable { get; set; }

    internal static bool TryParseScheme(
        MavlinkConnectionStringParts parts,
        out MavlinkEndpoint? endpoint,
        out string? error)
    {
        endpoint = null;
        error = null;

        var body = parts.Body;
        var name = body;
        int baudFromPath = -1;

        // "COM3:57600" / "/dev/ttyUSB0:115200" — trailing ":digits" is the baud rate.
        int idx = body.LastIndexOf(':');
        if (idx > 0 && idx < body.Length - 1
            && int.TryParse(
                body.Substring(idx + 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var b))
        {
            name = body.Substring(0, idx);
            baudFromPath = b;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Serial port name is empty. Expected e.g. 'serial:COM3?baud=57600' or 'serial:/dev/ttyUSB0:115200'.";
            return false;
        }

        var ep = new SerialEndpoint(name);

        if (baudFromPath > 0)
        {
            ep.BaudRate = baudFromPath;
        }

        if (ConnectionStringHelpers.TryGetInt(parts.Query, "baud", out var baud) && baud > 0)
        {
            ep.BaudRate = baud;
        }

        if (ConnectionStringHelpers.TryGetInt(parts.Query, "databits", out var dataBits)
            || ConnectionStringHelpers.TryGetInt(parts.Query, "data", out dataBits))
        {
            ep.DataBits = dataBits;
        }

        if (parts.Query.TryGetValue("parity", out var parity))
        {
            switch (parity.ToLowerInvariant())
            {
                case "none": ep.Parity = Parity.None; break;
                case "odd": ep.Parity = Parity.Odd; break;
                case "even": ep.Parity = Parity.Even; break;
                case "mark": ep.Parity = Parity.Mark; break;
                case "space": ep.Parity = Parity.Space; break;
                default:
                    error = $"Unknown parity '{parity}'.";
                    return false;
            }
        }

        if (parts.Query.TryGetValue("stopbits", out var stop)
            || parts.Query.TryGetValue("stop", out stop))
        {
            switch (stop)
            {
                case "1": ep.StopBits = StopBits.One; break;
                case "1.5": ep.StopBits = StopBits.OnePointFive; break;
                case "2": ep.StopBits = StopBits.Two; break;
                default:
                    error = $"Unknown stop bits '{stop}' (expected 1, 1.5 or 2).";
                    return false;
            }
        }

        if (ConnectionStringHelpers.TryGetBool(parts.Query, "dtr", out var dtr))
        {
            ep.DtrEnable = dtr;
        }

        if (ConnectionStringHelpers.TryGetBool(parts.Query, "rts", out var rts))
        {
            ep.RtsEnable = rts;
        }

        endpoint = ep;
        return true;
    }

    public override IMavlinkPortProvider CreateProvider()
    {
        var portName = PortName;
        var baud = BaudRate;
        var dataBits = DataBits;
        var parity = Parity;
        var stopBits = StopBits;
        var handshake = Handshake;
        var dtr = DtrEnable;
        var rts = RtsEnable;

        return new DelegatePortProvider(ct =>
        {
            var serial = new SerialPort(portName, baud, parity, dataBits, stopBits)
            {
                Handshake = handshake,
                DtrEnable = dtr,
                RtsEnable = rts,
                // Blocking timeouts are irrelevant: async I/O over BaseStream,
                // cancellation by closing the port.
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = SerialPort.InfiniteTimeout,
            };

            try
            {
                ct.ThrowIfCancellationRequested();
                serial.Open();

                return new ValueTask<IMavlinkPort>(new MavlinkStreamPort(
                    serial.BaseStream,
                    owner: serial,
                    cancelHook: () => serial.Close(),
                    exposeReader: false));
            }
            catch
            {
                serial.Dispose();
                throw;
            }
        });
    }

    public override string ToString() => $"serial:{PortName}:{BaudRate}";
}
