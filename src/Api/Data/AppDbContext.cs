using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunItem> RunItems => Set<RunItem>();
    public DbSet<Check> Checks => Set<Check>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<BlacklistEntry> BlacklistEntries => Set<BlacklistEntry>();
    public DbSet<BlacklistMeta> BlacklistMeta => Set<BlacklistMeta>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Merchant>().ToTable("merchants");

        b.Entity<Run>().ToTable("runs");

        b.Entity<RunItem>(e =>
        {
            e.ToTable("run_items");
            e.HasKey(x => new { x.RunId, x.MerchantId });
            e.HasOne<Run>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Merchant>().WithMany().HasForeignKey(x => x.MerchantId);
        });

        b.Entity<Check>(e =>
        {
            e.ToTable("checks");
            e.HasOne<Run>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Merchant>().WithMany().HasForeignKey(x => x.MerchantId);
            e.HasIndex(x => new { x.MerchantId, x.StartedAt });
        });

        b.Entity<Finding>(e =>
        {
            e.ToTable("findings");
            e.Property(x => x.Id).HasMaxLength(32);
            e.HasOne<Merchant>().WithMany().HasForeignKey(x => x.MerchantId);
            e.HasIndex(x => new { x.MerchantId, x.Url }).IsUnique();
            e.HasIndex(x => new { x.Resolved, x.LastSeenAt });
        });

        b.Entity<BlacklistEntry>(e =>
        {
            e.ToTable("blacklist_entries");
            e.HasKey(x => x.Url);
        });

        b.Entity<BlacklistMeta>(e =>
        {
            e.ToTable("blacklist_meta");
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        // snake_case column names (merchant_id, last_seen_at, ...) without an extra package.
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
