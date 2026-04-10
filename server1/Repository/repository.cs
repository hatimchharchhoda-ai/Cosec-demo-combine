using MatGenServer.Models;

namespace MatGenServer.Repositories.Interfaces;

public interface IDeviceRepository
{
    Task<Mat_DeviceMst?> GetByMACAndIPAsync(string macAddr, string ipAddr);
    Task<Mat_DeviceMst?> GetByDeviceIDAsync(int deviceId);
}

public interface IUserRepository
{
    Task<Mat_UserMst?> GetByUserIDAsync(string userId);
}

public interface ICommTrnRepository
{
    /// <summary>
    /// Fetch all TrnStat=0 records (max <paramref name="batchSize"/>).
    /// Atomically marks them as TrnStat=1 (Dispatched) so a concurrent poll
    /// from the SAME device won't return the same rows again.
    /// </summary>
    Task<List<Mat_CommTrn>> FetchAndMarkDispatchedAsync(string deviceId, int batchSize = 100);

    /// <summary>Returns the currently dispatched (TrnStat=1) batch for a device, if any.</summary>
    Task<List<Mat_CommTrn>> GetDispatchedBatchAsync(string deviceId);

    /// <summary>Mark a list of TrnIDs as Acknowledged (TrnStat=2).</summary>
    Task<int> MarkAcknowledgedAsync(IEnumerable<decimal> trnIds, string deviceId);

    /// <summary>Reset stuck dispatched records back to pending after timeout.</summary>
    Task ResetStalledDispatchesAsync(int timeoutMinutes = 5);
}