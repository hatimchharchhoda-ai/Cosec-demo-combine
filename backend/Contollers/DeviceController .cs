using COSEC_demo.DTOs;
using COSEC_demo.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace COSEC_demo.Contollers
{
    [Authorize]
    [ApiController]
    [Route("api/device")]
    public class DeviceController : ControllerBase
    {
        private readonly IDeviceService _service;

        public DeviceController(IDeviceService service)
        {
            _service = service;
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActiveDevices(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _service.GetActiveDevicesPaged(pageNumber, pageSize);

            return Ok(new ApiResponseDto<PagedResultDto<DeviceResponseDto>>
            {
                Success = true,
                Message = "Active devices fetched successfully",
                Data = result
            });
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAllDevices()
        {
            var result = await _service.GetAllDevices();
            return Ok(new ApiResponseDto<List<DeviceResponseDto>>
            {
                Success = true,
                Message = "All devices fetched successfully",
                Data = result
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _service.GetDeviceById(id);
            return Ok(new ApiResponseDto<DeviceResponseDto>
            {
                Success = true,
                Message = "Device fetched successfully",
                Data = result
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DeviceRequestDto dto)
        {
            var result = await _service.CreateDevice(dto);
            return Ok(new ApiResponseDto<DeviceResponseDto>
            {
                Success = true,
                Message = "Device created successfully",
                Data = result
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] DeviceRequestDto dto)
        {
            var result = await _service.UpdateDevice(id, dto);
            return Ok(new ApiResponseDto<DeviceResponseDto>
            {
                Success = true,
                Message = "Device updated successfully",
                Data = result
            });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var success = await _service.DeleteDevice(id);
            if (!success)
                return NotFound(new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Device not found",
                    Data = null
                });

            return Ok(new ApiResponseDto<object>
            {
                Success = true,
                Message = "Device deleted successfully",
                Data = null
            });
        }
    }
}
