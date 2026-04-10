using COSEC_demo.Entities;

namespace COSEC_demo.Repositories.Interfaces
{
    public interface ILoginRepository
    {
        Task<LoginUser> GetUser(string userId, string password);
    }
}
