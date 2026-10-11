using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Numera.Application;
using Numera.Application.Auth;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;
using Numera.Infrastructure.Persistence;
using Numera.Infrastructure.Seeding;

namespace Numera.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the DbContext (connection string <c>ConnectionStrings:Numera</c>), JWT issuing
    /// (<c>Jwt</c> section), password hashing and the demo seeders.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NumeraDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Numera")));
        services.AddScoped<INumeraDbContext>(sp => sp.GetRequiredService<NumeraDbContext>());

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddScoped<DemoSeeder>();
        services.AddScoped<DemoUserSeeder>();
        return services;
    }
}
