using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Anthropic;
using Microsoft.Extensions.Options;
using Numera.Application;
using Numera.Application.Ask;
using Numera.Application.Auth;
using Numera.Application.Entities;
using Numera.Infrastructure.Ask;
using Numera.Infrastructure.Auth;
using Numera.Infrastructure.Imports;
using Numera.Infrastructure.Metrics;
using Numera.Infrastructure.Orders;
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
        services.AddScoped<ImportOrdersHandler>();
        services.AddScoped<GetMetricsSummaryHandler>();
        services.AddScoped<GetRevenueTimeseriesHandler>();
        services.AddScoped<GetRevenueDriversHandler>();
        services.AddScoped<GetRevenueBreakdownHandler>();
        services.AddScoped<GetFilterOptionsHandler>();
        services.AddScoped<GetOrdersHandler>();
        services.AddScoped<ExportOrdersHandler>();

        // Ask in plain English (M17): off unless Ai:ApiKey is set. The client is only built when a question is asked.
        services.AddOptions<AskOptions>().Bind(configuration.GetSection(AskOptions.Section));
        services.AddSingleton<AskDailyBudget>();
        services.AddScoped<AskHandler>();
        services.AddSingleton(sp =>
        {
            var ask = sp.GetRequiredService<IOptions<AskOptions>>().Value;
            return new AnthropicClient { ApiKey = ask.ApiKey, Timeout = TimeSpan.FromSeconds(ask.TimeoutSeconds), MaxRetries = 1 };
        });
        services.TryAddScoped<IAskModel, ClaudeAskModel>();
        return services;
    }
}
