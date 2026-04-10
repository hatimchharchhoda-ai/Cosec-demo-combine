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
                var result = await _service.CreateCommTrn(dto);

                return Ok(new ApiResponseDto<CommTrnResponseDto>
                {
                    Success = true,
                    Message = "CommTrn created successfully",
                    Data = result
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ApiResponseDto<object>
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ApiResponseDto<object>
                {
                    Success = false,
                    Message = ex.InnerException?.Message ?? ex.Message,
                    Data = null
                });
            }
        }
    }
}