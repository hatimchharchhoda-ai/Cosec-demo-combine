using COSEC_demo.DTOs;
using COSEC_demo.Helpers;
using COSEC_demo.Repositories.Interfaces;
using COSEC_demo.Services.Interfaces;

namespace COSEC_demo.Services
{
    public class LoginService : ILoginService
    {
        private readonly ILoginRepository _repo;
        private readonly JwtHelper _jwtHelper;

        public LoginService(ILoginRepository repo, JwtHelper jwtHelper)
        {
            _repo = repo;
            _jwtHelper = jwtHelper;
        }

        public async Task<LoginResponseDto> Login(LoginRequestDto request)
        {
            var user = await _repo.GetUser(request.LoginUserID, request.LoginPassword);

            if (user == null)
                throw new Exception("Invalid credentials");

            var token = _jwtHelper.GenerateToken(user.LoginUserID);

            return new LoginResponseDto
            {
                UserId = user.LoginUserID,
                Token = token
            };
        }
    }
}