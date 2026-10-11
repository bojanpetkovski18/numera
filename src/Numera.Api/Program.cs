using Microsoft.EntityFrameworkCore;
using Numera.Infrastructure;
using Numera.Infrastructure.Persistence;
using Numera.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();

builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

// Maintenance commands: `dotnet run --project src/Numera.Api -- migrate` or `-- seed` (migrate, then reset and seed).
if (args.Contains("migrate") || args.Contains("seed"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
    await db.Database.MigrateAsync();
    if (args.Contains("seed"))
    {
        await scope.ServiceProvider.GetRequiredService<DemoSeeder>().ResetAndSeedAsync();
    }

    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;

