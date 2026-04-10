using Dapper;
using MatGenServer.Data;
using MatGenServer.Models;
using MatGenServer.Repositories.Interfaces;

namespace MatGenServer.Repositories;

public class DeviceRepository : IDeviceRepository
{
    private readonly AppDbContext _db;

    public DeviceRepository(AppDbContext db) => _db = db;

    public async Task<Mat_DeviceMst?> GetByMACAndIPAsync(string macAddr, string ipAddr)
    {
        const string sql = """
            SELECT DeviceID, DeviceName, MACAddr, IPAddr, IsActive, DeviceType
            FROM   dbo.Mat_DeviceMst
            WHERE  MACAddr = @MACAddr
              AND  IPAddr  = @IPAddr
            """;

        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Mat_DeviceMst>(sql, new { MACAddr = macAddr, IPAddr = ipAddr });
    }

    public async Task<Mat_DeviceMst?> GetByDeviceIDAsync(int deviceId)
    {
        const string sql = """
            SELECT DeviceID, DeviceName, MACAddr, IPAddr, IsActive, DeviceType
            FROM   dbo.Mat_DeviceMst
            WHERE  DeviceID = @DeviceID
            """;

        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Mat_DeviceMst>(sql, new { DeviceID = deviceId });
    }
}

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db) => _db = db;

    public async Task<Mat_UserMst?> GetByUserIDAsync(string userId)
    {
        const string sql = """
            SELECT UserID, UserName, IsActive, UserShortName, UserIDN
            FROM   dbo.Mat_UserMst
            WHERE  UserID = @UserID
            """;

        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Mat_UserMst>(sql, new { UserID = userId });
    }
}