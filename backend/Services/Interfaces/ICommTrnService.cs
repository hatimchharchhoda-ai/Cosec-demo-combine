﻿using COSEC_demo.DTOs;

namespace COSEC_demo.Services.Interfaces
{
    public interface ICommTrnService
    {
        Task<List<CommTrnResponseDto>> CreateCommTrnForAllUsers(int deviceId, string typeMid);
    }
}