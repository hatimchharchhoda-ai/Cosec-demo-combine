namespace MatPoll.Services;

// Simple thread-safe dictionary: deviceId → batchToken
// Singleton — lives for the lifetime of the server
//
// WHY: When server sends 50 rows to device (TrnStat=1),
//      we store a BatchToken here.
//      If device polls AGAIN before ACKing, we see the token
//      is still here and tell the device "ACK first!"
//      After device ACKs, we remove the token.

public class BatchCache
{
    private readonly Dictionary<decimal, string> _store = new();
    private readonly object _lock = new();

    public void Set(decimal deviceId, string batchToken)
    {
        lock (_lock)
            _store[deviceId] = batchToken;
    }

    public string? Get(decimal deviceId)
    {
        lock (_lock)
            return _store.TryGetValue(deviceId, out var t) ? t : null;
    }

    public void Remove(decimal deviceId)
    {
        lock (_lock)
            _store.Remove(deviceId);
    }

    public bool Has(decimal deviceId)
    {
        lock (_lock)
            return _store.ContainsKey(deviceId);
    }
}
