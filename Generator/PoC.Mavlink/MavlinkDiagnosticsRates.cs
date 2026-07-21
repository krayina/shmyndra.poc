namespace Mavlink;

public readonly record struct MavlinkDiagnosticsRates(
    double RxPacketsPerSec,
    double TxPacketsPerSec,
    double RxBytesPerSec,
    double TxBytesPerSec,
    double LossPercent);
