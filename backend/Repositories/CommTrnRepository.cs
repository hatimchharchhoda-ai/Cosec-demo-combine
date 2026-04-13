using COSEC_demo.Data;
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

        public async Task AddCommTrnRange(List<CommTrn> list)
        {
            await _context.CommTrns.AddRangeAsync(list);
            await _context.SaveChangesAsync();
        }
    }
}
