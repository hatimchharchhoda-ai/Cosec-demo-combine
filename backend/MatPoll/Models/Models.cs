using System.Collections.Generic;

namespace MatPoll.Models;

// ── Result objects returned from Repository ──────────────────────────────────
// These carry richer data back to the controller for logging purposes.

public class AckResult
{
    // How many rows were actually updated to TrnStat=2
    public int UpdatedCount { get; set; }

    // TrnIDs the client sent that we could NOT find/update
    // (wrong TypeMID, already ACKed, or never existed)
    public List<decimal> MismatchedIds { get; set; } = new();

    // Per-row ACK delay in seconds (TrnID → delay)
    public Dictionary<decimal, double> AckDelays { get; set; } = new();
}

public class StalledGroup
{
    public decimal DeviceID { get; set; }
    public int     RowCount   { get; set; }
    public int     MaxRetry   { get; set; }
    public int     ResetCount { get; set; }
    public int     FailedCount { get; set; }
}
