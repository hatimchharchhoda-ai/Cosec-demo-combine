using COSEC_demo.Data;
using COSEC_demo.Entities;
using COSEC_demo.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace COSEC_demo.Repositories
{
    public class LoginRepository : ILoginRepository
    {
        private readonly AppDbContext _context;

        public LoginRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<LoginUser> GetUser(string userId, string password)
        {
            return await _context.LoginUsers
                .FirstOrDefaultAsync(x =>
                    x.LoginUserID == userId &&
                    x.LoginPassword == password &&
                    x.IsActive == 1);
        }
    }
}
