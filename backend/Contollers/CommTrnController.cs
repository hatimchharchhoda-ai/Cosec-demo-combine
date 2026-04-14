using COSEC_demo.DTOs;
using COSEC_demo.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace COSEC_demo.Contollers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CommTrnController : ControllerBase
    {
        private readonly ICommTrnService _service;

        public CommTrnController(ICommTrnService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CommTrnRequestDto dto)
        {
            try
            {
                var result = await _service.CreateCommTrnForAllUsers(dto.DeviceId, dto.TypeMID);

                return Ok(new ApiResponseDto<object>
                {
                    Success = true,
                    Message = $"CommTrn created for {result.Count} users",
                    Data = null
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}