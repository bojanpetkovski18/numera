using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http.Features;
using Numera.Api.Auth;
using Numera.Api.Imports;
using Numera.Application;
using Numera.Application.Imports;
using Numera.Infrastructure;
using Numera.Infrastructure.Auth;
using Numera.Infrastructure.Persistence;
using Numera.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();

builder.Services.AddProblemDetails();

// Upload limits (spec: 10 MB). Each multipart part is capped at the file limit; the request body gets a little
// headroom for the multipart envelope.
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = ImportLimits.MaxFileBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ImportLimits.MaxFileBytes + 1024 * 1024);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

// Every endpoint requires a signed-in user unless it opts out with AllowAnonymous (health, login).
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(_ => { });
builder.Services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((limiter, configuration) =>
{
    var permitLimit = configuration.GetValue("RateLimiting:LoginPermitLimit", 10);
    var windowSeconds = configuration.GetValue("RateLimiting:LoginWindowSeconds", 60);

    limiter.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
            }));

    limiter.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }

        await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many login attempts",
                detail: "Wait a moment before trying again.")
            .ExecuteAsync(context.HttpContext);
    };
});

var app = builder.Build();

// Maintenance commands: `dotnet run --project src/Numera.Api -- migrate` or `-- seed` (migrate, then reset and seed).
// Both ensure the demo user exists.
if (args.Contains("migrate") || args.Contains("seed"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DemoUserSeeder>().EnsureAsync();
    if (args.Contains("seed"))
    {
        await scope.ServiceProvider.GetRequiredService<DemoSeeder>().ResetAndSeedAsync();
    }

    return;
}

// Fail fast with a clear message if the JWT settings (notably Jwt__SigningKey) are missing or too short.
_ = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();
app.MapAuthEndpoints();
app.MapImportEndpoints();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;
