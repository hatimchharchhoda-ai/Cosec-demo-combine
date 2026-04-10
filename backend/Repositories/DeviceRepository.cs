using COSEC_demo.Data;
using COSEC_demo.Entities;
using COSEC_demo.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace COSEC_demo.Repositories
{
    public class DeviceRepository : IDeviceRepository
    {
        private readonly AppDbContext _context;
        public DeviceRepository(AppDbContext context)
        {
            _context = context;
        }

        // Get all active devices
        public async Task<(List<Device>, int)> GetActiveDevicesPaged(int pageNumber, int pageSize)
        {
            var query = _context.Devices
                .Where(d => d.IsActive == 1);

            var totalRecords = await query.CountAsync();

            var devices = await query
                .OrderBy(d => d.DeviceID)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (devices, totalRecords);
        }

        // Get all devices (active + inactive)
        public async Task<List<Device>> GetAllDevices()
        {
            return await _context.Devices.ToListAsync();
        }

        public async Task<Device> GetDeviceById(int id)
        {
            return await _context.Devices
                .FirstOrDefaultAsync(d => d.DeviceID == id);
        }

        public async Task<Device> AddDevice(Device device)
        {
            _context.Devices.Add(device);
            await _context.SaveChangesAsync();
            return device;
        }

        public async Task<Device> UpdateDevice(Device device)
        {
            _context.Devices.Update(device);
            await _context.SaveChangesAsync();
            return device;
        }

        public async Task<bool> SoftDeleteDevice(int id)
        {
            var device = await _context.Devices.FirstOrDefaultAsync(d => d.DeviceID == id);
            if (device == null) return false;

            device.IsActive = 0; // soft delete
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
