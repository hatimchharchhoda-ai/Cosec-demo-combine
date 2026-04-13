using COSEC_demo.Data;
using COSEC_demo.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace COSEC_demo.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _context;

        public UserRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<string>> GetActiveUserIds()
        {
            return await _context.MatUserMsts
                .Where(x => x.isActive == 1)
                .Select(x => x.UserId)
                .ToListAsync();
        }
    }
}
