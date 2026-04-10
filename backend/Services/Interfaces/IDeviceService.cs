using COSEC_demo.DTOs;

namespace COSEC_demo.Services.Interfaces
{
    public interface IDeviceService
    {
        Task<PagedResultDto<DeviceResponseDto>> GetActiveDevicesPaged(int pageNumber, int pageSize);
        Task<List<DeviceResponseDto>> GetAllDevices();
        Task<DeviceResponseDto> GetDeviceById(int id);
        Task<DeviceResponseDto> CreateDevice(DeviceRequestDto dto);
        Task<DeviceResponseDto> UpdateDevice(int id, DeviceRequestDto dto);
        Task<bool> DeleteDevice(int id);
    }
}
