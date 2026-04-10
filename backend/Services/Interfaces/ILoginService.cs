using COSEC_demo.DTOs;

namespace COSEC_demo.Services.Interfaces
{
    public interface ILoginService
    {
        Task<LoginResponseDto> Login(LoginRequestDto request);
    }
}
