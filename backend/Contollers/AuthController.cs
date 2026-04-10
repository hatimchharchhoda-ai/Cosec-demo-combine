using COSEC_demo.DTOs;
using COSEC_demo.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace COSEC_demo.Contollers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly ILoginService _service;

        public AuthController(ILoginService service)
        {
            _service = service;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequestDto request)
        {
            try
            {
                var result = await _service.Login(request);

                Response.Cookies.Append("jwtToken", result.Token, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.None,
                    Expires = DateTime.UtcNow.AddMinutes(60)
                });

                return Ok(new ApiResponseDto<LoginResponseDto>
                {
                    Success = true,
                    Message = "Login successful",
                    Data = new LoginResponseDto
                    {
                        UserId = result.UserId
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ApiResponseDto<object>
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null
                });
            }
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("auth_token", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            });
            return Ok(new ApiResponseDto<object> { Success = true, Message = "Logged out." });
        }


        [Authorize]
        [HttpGet("check")]
        public IActionResult CheckAuth()
        {
            var userId = User.Identity?.Name;
            return Ok(new { userId });
        }
    }
}
