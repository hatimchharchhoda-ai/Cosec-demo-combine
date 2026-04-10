using COSEC_demo.Data;
using COSEC_demo.DTOs;
using COSEC_demo.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NMatGen.API.Models;

namespace NMatGen.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UserController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/User/list?page=1&pageSize=10
        [HttpGet("list")]
        public async Task<IActionResult> GetUsers(int page = 1, int pageSize = 10)
        {
            try
            {
                var query = _context.MatUserMsts.AsQueryable();

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

                var users = await query
                    .OrderBy(u => u.UserId)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(u => new UserDto
                    {
                        UserId = u.UserId,
                        UserName = u.UserName,
                        IsActive = u.isActive == 1,
                        UserShortName = u.UserShortName,
                        UserIDN = u.UserIDN
                    })
                    .ToListAsync();

                return Ok(new ApiResponseDto<UserListResponseDto>
                {
                    Success = true,
                    Data = new UserListResponseDto
                    {
                        Users = users,
                        TotalCount = totalCount,
                        Page = page,
                        PageSize = pageSize,
                        TotalPages = totalPages
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500,
                    new ApiResponseDto<object> { Success = false, Message = ex.Message });
            }
        }

        // GET: api/User/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(string id)
        {
            var user = await _context.MatUserMsts.FindAsync(id);

            if (user == null)
                return NotFound(new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "User not found"
                });

            return Ok(new ApiResponseDto<UserDto>
            {
                Success = true,
                Data = new UserDto
                {
                    UserId = user.UserId,
                    UserName = user.UserName,
                    IsActive = user.isActive == 1,
                    UserShortName = user.UserShortName,
                    UserIDN = user.UserIDN
                }
            });
        }

        // POST: api/User/add
        [HttpPost("add")]
        public async Task<IActionResult> AddUser([FromBody] UserDto dto)
        {
            try
            {
                var user = new MatUserMst
                {
                    UserId = dto.UserId,
                    UserName = dto.UserName,
                    isActive = dto.IsActive ? 1 : 0,
                    UserShortName = dto.UserShortName,
                    UserIDN = dto.UserIDN
                };

                _context.MatUserMsts.Add(user);
                await _context.SaveChangesAsync();

                return Ok(new ApiResponseDto<UserDto>
                {
                    Success = true,
                    Message = "User added successfully",
                    Data = dto
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500,
                    new ApiResponseDto<object> { Success = false, Message = ex.Message });
            }
        }

        // PUT: api/User/update
        [HttpPut("update")]
        public async Task<IActionResult> UpdateUser([FromBody] UserDto dto)
        {
            try
            {
                var user = await _context.MatUserMsts.FindAsync(dto.UserId);

                if (user == null)
                    return NotFound(new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });

                user.UserName = dto.UserName;
                user.isActive = dto.IsActive ? 1 : 0;
                user.UserShortName = dto.UserShortName;
                user.UserIDN = dto.UserIDN;

                await _context.SaveChangesAsync();

                return Ok(new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "User updated successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500,
                    new ApiResponseDto<object> { Success = false, Message = ex.Message });
            }
        }

        // DELETE: api/User/delete/{id}
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            try
            {
                var user = await _context.MatUserMsts.FindAsync(id);

                if (user == null)
                    return NotFound(new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });

                user.isActive = 0; // Soft delete by setting isActive to 0
                //_context.MatUserMst.Remove(user);
                await _context.SaveChangesAsync();


                return Ok(new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "User deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500,
                    new ApiResponseDto<object> { Success = false, Message = ex.Message });
            }
        }

        // POST: api/User/enroll/{id}
        [HttpPost("enroll/{id}")]
        public async Task<IActionResult> EnrollUser(string id)
        {
            try
            {
                var user = await _context.MatUserMsts.FindAsync(id);

                if (user == null)
                    return NotFound(new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });

                var trn = new CommTrn
                {
                    MsgStr = $"User Enroll with UserId {id}",
                    RetryCnt = 0m,
                    TrnStat = 0m,
                    CreatedAt = DateTime.Now,
                };

                _context.CommTrns.Add(trn);
                await _context.SaveChangesAsync();

                return Ok(new ApiResponseDto<object>
                {
                    Success = true,
                    Message = $"User {id} enrolled successfully. Transaction ID: {trn.TrnID}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ApiResponseDto<object>
                {
                    Success = false,
                    Message = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

    }
}