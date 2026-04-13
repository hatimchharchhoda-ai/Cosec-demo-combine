﻿using COSEC_demo.Data;
using COSEC_demo.Entities;
using COSEC_demo.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace COSEC_demo.Repositories
{
    public class CommTrnRepository : ICommTrnRepository
    {
        private readonly AppDbContext _context;

        public CommTrnRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<CommTrn> AddCommTrn(CommTrn commTrn)
        {
            await _context.CommTrns.AddAsync(commTrn);
            await _context.SaveChangesAsync();
            return commTrn;
        }
    }
}