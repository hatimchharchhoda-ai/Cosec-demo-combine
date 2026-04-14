using MatPoll.Models;
using Microsoft.EntityFrameworkCore;

namespace MatPoll.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Each DbSet = one table
    public DbSet<MatDeviceMst> Devices  { get; set; }
    public DbSet<MatUserMst>   Users    { get; set; }
    public DbSet<MatCommTrn>   CommTrns { get; set; }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Mat_DeviceMst — DeviceID is NOT identity in your table (no IDENTITY keyword)
        b.Entity<MatDeviceMst>()
            .Property(d => d.DeviceID)
            .ValueGeneratedNever();

        // Mat_CommTrn — TrnID IS identity
        b.Entity<MatCommTrn>()
            .Property(t => t.TrnID)
            .ValueGeneratedOnAdd();

        b.Entity<MatCommTrn>()
        .Property(x => x.CreatedAt)
        .IsConcurrencyToken(false);
    }
}
