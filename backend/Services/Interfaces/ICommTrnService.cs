using COSEC_demo.DTOs;

namespace COSEC_demo.Services.Interfaces
{
    public interface ICommTrnService
    {
        Task<string> CreateCommTrn(CommTrnRequestDto dto);
    }
}
