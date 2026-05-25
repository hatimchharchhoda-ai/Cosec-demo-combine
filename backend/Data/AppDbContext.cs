using COSEC_demo.Entities;
using Microsoft.EntityFrameworkCore;
using NMatGen.API.Models;

namespace COSEC_demo.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<LoginUser> LoginUsers { get; set; }
        public DbSet<Device> Devices { get; set; }
        public DbSet<CommTrn> CommTrns { get; set; }
        public DbSet<MatUserMst> MatUserMsts { get; set; }
        public DbSet<DeviceEvent> DeviceEvents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<LoginUser>()
                .HasKey(x => x.LoginUserID);

            modelBuilder.Entity<LoginUser>()
                .ToTable("Mat_LoginUserMst", "dbo");

            modelBuilder.Entity<Device>()
                .ToTable("Mat_DeviceMst", "dbo");

            modelBuilder.Entity<Device>()
                .Property(d => d.DeviceID)
                .ValueGeneratedNever();

            modelBuilder.Entity<CommTrn>()
                .ToTable("Mat_CommTrn", "dbo");

            modelBuilder.Entity<CommTrn>()
                .Property(t => t.TrnID)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<MatUserMst>()
                .ToTable("Mat_UserMst", "dbo");

            modelBuilder.Entity<DeviceEvent>()
                .ToTable("Mat_DeviceEvent", "dbo");

            modelBuilder.Entity<DeviceEvent>()
                .Property(e => e.EventID)
                .ValueGeneratedOnAdd();
        }
    }
}
