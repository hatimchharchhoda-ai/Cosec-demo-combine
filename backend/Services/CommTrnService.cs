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
        private readonly IUserRepository _userRepo;

        public CommTrnService(ICommTrnRepository repo, IUserRepository userRepo)
        {
            _repo = repo;
            _userRepo = userRepo;
        }

        public async Task<string> CreateCommTrn(CommTrnRequestDto dto)
        {
            if (dto.DeviceId <= 0)
                throw new ArgumentException("DeviceId is required.");

            // 1. Fetch all active users
            var userIds = await _userRepo.GetActiveUserIds();

            if (!userIds.Any())
                throw new Exception("No active users found.");

            var commList = new List<CommTrn>();

            // 2. Create entry for each user
            foreach (var userId in userIds)
            {
<<<<<<< HEAD
                MsgStr = message,
                RetryCnt = 0,
                TrnStat = 0,
                CreatedAt = DateTime.Now,
            };
=======
                string message = BuildMessage(userId, dto.DeviceId);
>>>>>>> 89794c4 (WIP: my local changes before merging server branch)

                commList.Add(new CommTrn
                {
                    MsgStr = message,
                    RetryCnt = 0,
                    TrnStat = 0,
                    CreatedAt = DateTime.Now
                });
            }

            // 3. Bulk insert
            await _repo.AddCommTrnRange(commList);

            return $"{commList.Count} CommTrn records created successfully.";
        }

        private string BuildMessage(string userId, int deviceId)
        {
            return $"UID:{userId} | DID:{deviceId} | CMD:SYNC";
        }
    }
}
