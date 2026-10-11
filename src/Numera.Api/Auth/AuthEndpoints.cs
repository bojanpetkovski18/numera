using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using Numera.Application.Auth;

namespace Numera.Api.Auth;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    private const int MaxEmailLength = 254;
    private const int MaxPasswordLength = 256;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy);

        // Lets the client confirm that its token is still accepted.
        group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new { email = user.FindFirstValue("email") }));

        return app;
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, LoginHandler handler, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > MaxEmailLength)
        {
            errors["email"] = [$"Email is required and must be at most {MaxEmailLength} characters."];
        }

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > MaxPasswordLength)
        {
            errors["password"] = [$"Password is required and must be at most {MaxPasswordLength} characters."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var token = await handler.HandleAsync(new LoginCommand(request.Email!, request.Password!), ct);
        return token is null
            ? Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "The email or password is incorrect.")
            : Results.Ok(new LoginResponse(token.Value, "Bearer", token.ExpiresAt));
    }
}
