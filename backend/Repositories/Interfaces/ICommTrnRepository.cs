using COSEC_demo.Entities;

namespace COSEC_demo.Repositories.Interfaces
{
    public interface ICommTrnRepository
    {
        Task AddCommTrnRange(List<CommTrn> list);
    }
}
