using Microsoft.EntityFrameworkCore;
using Numera.Application.Entities;

namespace Numera.Application;

/// <summary>
/// The slice of the database that command and query handlers use. Implemented by the Infrastructure DbContext,
/// so handlers stay in Application while EF Core provider details stay in Infrastructure.
/// </summary>
public interface INumeraDbContext
{
    DbSet<Order> Orders { get; }
    DbSet<ImportJob> ImportJobs { get; }
    DbSet<ImportError> ImportErrors { get; }
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
