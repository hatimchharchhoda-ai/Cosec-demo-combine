using MatGenServer.DTOs;
using MatGenServer.Repositories.Interfaces;
using MatGenServer.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace MatGenServer.Services { }

/// <summary>
/// POLLING STRATEGY – handles the "100 records in-flight" race:
///
/// Flow:
///   Client polls every 8s  →  PollAsync
///     ├─ Is there already a TrnStat=1 batch for this device that is NOT yet ACKed?
///     │      YES → return PendingAckRequired=true + PendingBatchToken
///     │              Client must ACK the old batch before new data is sent.
///     │              (Prevents duplicate processing; batch token acts as idempotency key)
///     │
///     └─ No pending batch → FetchAndMarkDispatchedAsync (atomic UPDATE in DB)
///            → return new batch  (HasData=true, BatchToken=<guid>)
///            → or empty response (HasData=false) if nothing to send
///
///   Client sends ACK  →  AcknowledgeAsync
///     → MarkAcknowledgedAsync (TrnStat 1→2) – only updates if DeviceID matches
///     → Remove batch token from cache
///
/// BatchToken:
///   A server-generated GUID stored in IMemoryCache keyed by device.
///   It ties a list of TrnIDs to a single delivery attempt so the
///   ACK call can be validated cheaply without another DB round-trip.
/// </summary>
public class PollingService : IPollingService
{
    private readonly ICommTrnRepository _commTrnRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PollingService> _logger;

    // Cache entry: stores TrnIDs for a pending batch per device
    private record BatchCacheEntry(string BatchToken, List<decimal> TrnIDs, DateTime CreatedAt);

    public PollingService(
        ICommTrnRepository commTrnRepo,
        IMemoryCache cache,
        ILogger<PollingService> logger)
    {
        _commTrnRepo = commTrnRepo;
        _cache = cache;
        _logger = logger;
    }

    // ── Poll ──────────────────────────────────────────────────────────────────

    public async Task<PollResponseDto> PollAsync(string deviceId, PollRequestDto request)
    {
        var cacheKey = CacheKey(deviceId);

        // ── Step 1: Check if there's an un-ACKed batch in cache ──────────────
        if (_cache.TryGetValue(cacheKey, out BatchCacheEntry? pending) && pending is not null)
        {
            // Client is sending back the token of the batch it just processed?
            if (!string.IsNullOrEmpty(request.LastBatchToken) &&
                request.LastBatchToken == pending.BatchToken)
            {
                // Client already processed it locally but ACK endpoint wasn't
                // reached (network blip). Auto-ACK and fall through to next batch.
                _logger.LogInformation("Device {DeviceID}: auto-clearing stale cache entry via poll token match.", deviceId);
                _cache.Remove(cacheKey);
            }
            else
            {
                // There's an active batch the client hasn't ACKed yet.
                // Do NOT send more data – tell client to ACK first.
                _logger.LogWarning(
                    "Device {DeviceID}: pending batch {Token} not yet ACKed. Asking client to ACK first.",
                    deviceId, pending.BatchToken);

                return new PollResponseDto
                {
                    HasData = false,
                    PendingAckRequired = true,
                    PendingBatchToken = pending.BatchToken
                };
            }
        }

        // ── Step 2: Also cross-check DB for TrnStat=1 rows (covers server restart) ──
        var dispatchedInDb = await _commTrnRepo.GetDispatchedBatchAsync(deviceId);
        if (dispatchedInDb.Count > 0)
        {
            // Rows are stuck in dispatched state (e.g. cache lost after restart).
            // Rebuild the cache entry so the client is asked to ACK.
            var recoveredToken = Guid.NewGuid().ToString("N");
            var entry = new BatchCacheEntry(
                recoveredToken,
                dispatchedInDb.Select(t => t.TrnID).ToList(),
                DateTime.UtcNow);
            SetCache(cacheKey, entry);

            _logger.LogWarning(
                "Device {DeviceID}: found {Count} dispatched rows in DB without cache. Rebuilt batch token {Token}.",
                deviceId, dispatchedInDb.Count, recoveredToken);

            return new PollResponseDto
            {
                HasData = false,
                PendingAckRequired = true,
                PendingBatchToken = recoveredToken
            };
        }

        // ── Step 3: Fetch new pending records & atomically mark dispatched ────
        var items = await _commTrnRepo.FetchAndMarkDispatchedAsync(deviceId, batchSize: 100);

        if (items.Count == 0)
            return new PollResponseDto { HasData = false };

        // ── Step 4: Store batch in cache (TTL = 10 min) ───────────────────────
        var batchToken = Guid.NewGuid().ToString("N");
        var newEntry = new BatchCacheEntry(batchToken, items.Select(i => i.TrnID).ToList(), DateTime.UtcNow);
        SetCache(cacheKey, newEntry);

        _logger.LogInformation(
            "Device {DeviceID}: dispatched {Count} items, batch token {Token}.",
            deviceId, items.Count, batchToken);

        return new PollResponseDto
        {
            HasData = true,
            BatchToken = batchToken,
            Items = items.Select(i => new TrnItemDto
            {
                TrnID = i.TrnID,
                MsgStr = i.MsgStr,
                RetryCnt = i.RetryCnt
            }).ToList()
        };
    }

    // ── Acknowledge ───────────────────────────────────────────────────────────

    public async Task<AckResponseDto> AcknowledgeAsync(string deviceId, AckRequestDto request)
    {
        var cacheKey = CacheKey(deviceId);

        // Validate the batch token
        if (!_cache.TryGetValue(cacheKey, out BatchCacheEntry? pending) || pending is null)
        {
            // No cache entry – possibly already ACKed; try DB update anyway (idempotent)
            _logger.LogWarning("Device {DeviceID}: ACK received but no cache entry found. Attempting DB update.", deviceId);
        }
        else if (pending.BatchToken != request.BatchToken)
        {
            return new AckResponseDto
            {
                Success = false,
                Message = $"BatchToken mismatch. Expected {pending.BatchToken}, got {request.BatchToken}."
            };
        }

        // Update DB
        var updated = await _commTrnRepo.MarkAcknowledgedAsync(request.AckedTrnIDs, deviceId);

        // Clear cache – device is now free to receive next batch
        _cache.Remove(cacheKey);

        _logger.LogInformation(
            "Device {DeviceID}: ACKed {Count} records for batch {Token}.",
            deviceId, updated, request.BatchToken);

        return new AckResponseDto
        {
            Success = true,
            UpdatedCount = updated,
            Message = $"{updated} records acknowledged."
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string CacheKey(string deviceId) => $"poll_batch_{deviceId}";

    private void SetCache(string key, BatchCacheEntry entry)
    {
        _cache.Set(key, entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            Priority = CacheItemPriority.High
        });
    }
}