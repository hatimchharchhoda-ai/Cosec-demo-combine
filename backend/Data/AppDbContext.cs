using COSEC_demo.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace COSEC_demo.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<LoginUser> LoginUsers { get; set; }
        public DbSet<Device> Devices { get; set; }
        public DbSet<CommTrn> CommTrns { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LoginUser>().HasKey(x => x.LoginUserID);
        }
    }
}
