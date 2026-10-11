using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Auth;
using Numera.Application.Imports;

namespace Numera.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<CredentialVerifier>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<GetImportJobHandler>();
        return services;
    }
}
