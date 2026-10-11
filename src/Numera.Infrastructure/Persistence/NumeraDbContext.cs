using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Numera.Application;
using Numera.Application.Entities;

namespace Numera.Infrastructure.Persistence;

public partial class NumeraDbContext(DbContextOptions<NumeraDbContext> options) : DbContext(options), INumeraDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportError> ImportErrors => Set<ImportError>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(ConfigureOrder);
        modelBuilder.Entity<ImportJob>(ConfigureImportJob);
        modelBuilder.Entity<ImportError>(ConfigureImportError);
        modelBuilder.Entity<User>(ConfigureUser);

        // snake_case columns to match the spec; table names are set explicitly above.
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    private static void ConfigureOrder(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_orders_unit_price_non_negative", "unit_price >= 0");
            t.HasCheckConstraint("ck_orders_discount_range", "discount >= 0 AND discount <= 1");
            t.HasCheckConstraint("ck_orders_status_valid", "status IN ('completed', 'refunded', 'cancelled')");
        });
        b.HasKey(o => o.Id);
        b.Property(o => o.OrderId).HasMaxLength(40);
        b.Property(o => o.CustomerId).HasMaxLength(40);
        b.Property(o => o.Region).HasMaxLength(60);
        b.Property(o => o.Channel).HasMaxLength(60);
        b.Property(o => o.ProductSku).HasMaxLength(40);
        b.Property(o => o.ProductCategory).HasMaxLength(60);
        b.Property(o => o.UnitPrice).HasColumnType("numeric(12,2)");
        b.Property(o => o.Discount).HasColumnType("numeric(4,3)");
        b.Property(o => o.Status)
            .HasMaxLength(20)
            .HasConversion(s => OrderStatusMapping.ToDatabase(s), v => OrderStatusMapping.FromDatabase(v));

        b.HasIndex(o => new { o.OrderId, o.ProductSku }).IsUnique().HasDatabaseName("ux_orders_order_id_product_sku");
        b.HasIndex(o => o.OrderDate).HasDatabaseName("ix_orders_order_date");
        b.HasIndex(o => o.Region).HasDatabaseName("ix_orders_region");
        b.HasIndex(o => o.Channel).HasDatabaseName("ix_orders_channel");
        b.HasIndex(o => o.ProductCategory).HasDatabaseName("ix_orders_product_category");
        b.HasIndex(o => o.Status).HasDatabaseName("ix_orders_status");
        b.HasIndex(o => o.ImportJobId).HasDatabaseName("ix_orders_import_job_id");

        b.HasOne<ImportJob>().WithMany().HasForeignKey(o => o.ImportJobId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureImportJob(EntityTypeBuilder<ImportJob> b)
    {
        b.ToTable("import_jobs");
        b.HasKey(j => j.Id);
        b.Property(j => j.FileName).HasMaxLength(260);
        b.Property(j => j.Status)
            .HasMaxLength(20)
            .HasConversion(s => s.ToString().ToLowerInvariant(), v => Enum.Parse<ImportJobStatus>(v, true));
        b.Property(j => j.Warnings).HasColumnType("text[]");
        b.Property(j => j.FailureReason).HasMaxLength(500);
    }

    private static void ConfigureImportError(EntityTypeBuilder<ImportError> b)
    {
        b.ToTable("import_errors");
        b.HasKey(e => e.Id);
        b.Property(e => e.Field).HasMaxLength(60);
        b.Property(e => e.Message).HasMaxLength(500);
        b.Property(e => e.RawRow).HasColumnType("text");
        b.HasIndex(e => e.ImportJobId).HasDatabaseName("ix_import_errors_import_job_id");
        b.HasOne<ImportJob>().WithMany().HasForeignKey(e => e.ImportJobId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureUser(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Email).HasMaxLength(254);
        b.Property(u => u.PasswordHash).HasMaxLength(200);
        b.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ux_users_email");
    }

    private static string ToSnakeCase(string name) => PascalBoundary().Replace(name, "_$1").ToLowerInvariant();

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex PascalBoundary();
}
