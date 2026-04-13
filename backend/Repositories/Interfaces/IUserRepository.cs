namespace COSEC_demo.Repositories.Interfaces
{
    public interface IUserRepository
    {
        Task<List<string>> GetActiveUserIds();
    }
}
