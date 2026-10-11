using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Numera.Infrastructure.Persistence;
using Numera.Infrastructure.Seeding;

namespace Numera.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the DbContext (connection string <c>ConnectionStrings:Numera</c>) and the demo seeder.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NumeraDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Numera")));
        services.AddScoped<DemoSeeder>();
        return services;
    }
}
