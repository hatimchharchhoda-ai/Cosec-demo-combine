using System.Security.Cryptography;
using System.Text;
using COSEC_demo.DTOs;
using COSEC_demo.Entities;
using COSEC_demo.Repositories.Interfaces;
using COSEC_demo.Services.Interfaces;

namespace COSEC_demo.Services
{
    public class DeviceService : IDeviceService
    {
        private readonly IDeviceRepository _repo;

        public DeviceService(IDeviceRepository repo)
        {
            _repo = repo;
        }

        public async Task<PagedResultDto<DeviceResponseDto>> GetActiveDevicesPaged(int pageNumber, int pageSize)
        {
            var (devices, total) = await _repo.GetActiveDevicesPaged(pageNumber, pageSize);

            return new PagedResultDto<DeviceResponseDto>
            {
                Data = devices.Select(MapToDto).ToList(),
                TotalRecords = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<List<DeviceResponseDto>> GetAllDevices()
        {
            var devices = await _repo.GetAllDevices();
            return devices.Select(MapToDto).ToList();
        }

        public async Task<DeviceResponseDto> GetDeviceById(int id)
        {
            var device = await _repo.GetDeviceById(id);
            if (device == null)
                throw new Exception("Device not found");

            return MapToDto(device);
        }

        public async Task<DeviceResponseDto> CreateDevice(DeviceRequestDto dto)
        {
            var device = new Device
            {
                DeviceName = dto.DeviceName,
                MACAddr = dto.MACAddr,
                IPAddr = dto.IPAddr,
                DeviceType = dto.DeviceType,
                IsActive = dto.IsActive ? 1 : 0,
            };

            var created = await _repo.AddDevice(device);
            return MapToDto(created);
        }

        public async Task<DeviceResponseDto> UpdateDevice(int id, DeviceRequestDto dto)
        {
            var device = await _repo.GetDeviceById(id);
            if (device == null)
                throw new Exception("Device not found");

            device.DeviceName = dto.DeviceName;
            device.MACAddr = dto.MACAddr;
            device.IPAddr = dto.IPAddr;
            device.DeviceType = dto.DeviceType;
            device.IsActive = dto.IsActive ? 1 : 0;

            var updated = await _repo.UpdateDevice(device);
            return MapToDto(updated);
        }

        public async Task<bool> DeleteDevice(int id)
        {
            return await _repo.SoftDeleteDevice(id);
        }

        // Helper to map entity to DTO
        private DeviceResponseDto MapToDto(Device d)
        {
            return new DeviceResponseDto
            {
                DeviceID = (int)d.DeviceID,
                DeviceName = d.DeviceName,
                MACAddr = d.MACAddr,
                IPAddr = d.IPAddr,
                DeviceType = (int)d.DeviceType,
                IsActive = d.IsActive == 1,
                TypeMID = GenerateTypeMID(d.MACAddr, d.IPAddr) // generated here
            };
        }


        private string GenerateTypeMID(string mac, string ip)
        {
            var input = $"{mac}|{ip}";
            using var md5 = MD5.Create();
            var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

            var sb = new StringBuilder();
            foreach (var b in hashBytes)
                sb.Append(b.ToString("x2"));

            return sb.ToString().Substring(0, 12);
        }
    }
}
