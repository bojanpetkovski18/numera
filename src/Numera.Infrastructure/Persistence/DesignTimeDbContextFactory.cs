using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Numera.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to build the model. Never connects; the connection string is a placeholder.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NumeraDbContext>
{
    public NumeraDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NumeraDbContext>()
            .UseNpgsql("Host=localhost;Database=numera_design_time")
            .Options;
        return new NumeraDbContext(options);
    }
}
