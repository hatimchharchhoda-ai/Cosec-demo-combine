using COSEC_demo.Entities;

namespace COSEC_demo.Repositories.Interfaces
{
    public interface IDeviceRepository
    {
        Task<List<Device>> GetAllDevices();
        Task<(List<Device>, int)> GetActiveDevicesPaged(int pageNumber, int pageSize);
        Task<Device> GetDeviceById(int id);
        Task<bool> SoftDeleteDevice(int id);
        Task<Device> UpdateDevice(Device device);
        Task<Device> AddDevice(Device device);

    }
}
