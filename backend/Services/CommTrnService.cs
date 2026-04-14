﻿using COSEC_demo.DTOs;
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

        public async Task<List<CommTrnResponseDto>> CreateCommTrnForAllUsers(int deviceId, string typeMid)
        {
            var userIds = await _userRepo.GetActiveUserIds();

            var result = new List<CommTrnResponseDto>();

            foreach (var userId in userIds)
            {
                string message = BuildMessage(userId, deviceId);

                var entity = new CommTrn
                {
                    MsgStr = message,
                    RetryCnt = 0,
                    TrnStat = 0,
                    CreatedAt = DateTime.Now,
                    TypeMID = typeMid
                };

                var created = await _repo.AddCommTrn(entity);
                result.Add(MapToDto(created));
            }

            return result;
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
            return $"ENROLL|UID:{userId}|DID:{deviceId}";
        }
    }
}