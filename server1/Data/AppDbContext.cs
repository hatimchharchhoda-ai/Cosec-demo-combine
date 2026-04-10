using Microsoft.EntityFrameworkCore;
using MatGenServer.Models;

namespace MatGenServer.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

       
        public DbSet<Mat_DeviceMst> Devices { get; set; }
        public DbSet<Mat_CommTrn> CommTrns { get; set; }
        public DbSet<Mat_UserMst> MatUserMsts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            
        }
    }

}