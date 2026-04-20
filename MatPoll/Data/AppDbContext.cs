using MatPoll.Models;
using Microsoft.EntityFrameworkCore;

namespace MatPoll.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MatDeviceMst> Devices  { get; set; }
    public DbSet<MatUserMst>   Users    { get; set; }
    public DbSet<MatCommTrn>   CommTrns { get; set; }
    
    public DbSet<MatDeviceEvent> DeviceEvents { get; set; }
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MatDeviceMst>()
            .Property(d => d.DeviceID)
            .ValueGeneratedNever();

        b.Entity<MatCommTrn>()
            .Property(t => t.TrnID)
            .ValueGeneratedOnAdd();

        // DispatchedAt is not a concurrency token — plain datetime column
        // b.Entity<MatCommTrn>()
        //     .Property(t => t.DispatchedAt)
        //     .IsRequired(false);
    }
}
