using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Auth;

namespace Numera.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<CredentialVerifier>();
        services.AddScoped<LoginHandler>();
        return services;
    }
}
