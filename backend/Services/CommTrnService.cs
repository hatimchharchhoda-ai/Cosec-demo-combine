using COSEC_demo.DTOs;
using COSEC_demo.Entities;
using COSEC_demo.Repositories.Interfaces;
using COSEC_demo.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace COSEC_demo.Services
{
    public class CommTrnService : ICommTrnService
    {
        private readonly ICommTrnRepository _repo;

        public CommTrnService(ICommTrnRepository repo)
        {
            _repo = repo;
        }

        public async Task<CommTrnResponseDto> CreateCommTrn(CommTrnRequestDto dto)
        {
            if (dto.UserId.IsNullOrEmpty())
                throw new ArgumentException("UserId is required.");

            if (dto.DeviceId <= 0)
                throw new ArgumentException("DeviceId is required.");

            string message = BuildMessage(dto.UserId, dto.DeviceId);

            var entity = new CommTrn
            {
                MsgStr = message,
                RetryCnt = 0,
                TrnStat = 0,
                CreatedAt = DateTime.Now,
            };

            var created = await _repo.AddCommTrn(entity);

            return MapToDto(created);
        }

        private CommTrnResponseDto MapToDto(CommTrn c)
        {
            return new CommTrnResponseDto
            {
                TrnID = (int)c.TrnID,
                MsgStr = c.MsgStr,
                RetryCnt = (int)c.RetryCnt,
                TrnStat = (int)c.TrnStat
            };
        }

        private string BuildMessage(string userId, int deviceId)
        {
            return $"UID:{userId} | DID:{deviceId} | CMD:SYNC";
        }
    }
}
