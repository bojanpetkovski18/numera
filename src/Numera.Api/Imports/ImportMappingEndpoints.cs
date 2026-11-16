using System.Security.Claims;
using Numera.Application.Imports;

namespace Numera.Api.Imports;

/// <summary>A signed-in user's saved column mappings. Another user's mapping is always "not found".</summary>
public static class ImportMappingEndpoints
{
    public static IEndpointRouteBuilder MapImportMappingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/import-mappings");

        group.MapGet("/", async (ClaimsPrincipal user, SavedMappingsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListAsync(user.UserId(), ct)));

        group.MapPost("/", async (SaveMappingRequest body, ClaimsPrincipal user, SavedMappingsHandler handler, CancellationToken ct) =>
            ToResult(await handler.CreateAsync(user.UserId(), body, ct), created: true));

        group.MapPut("/{id:guid}", async (Guid id, SaveMappingRequest body, ClaimsPrincipal user, SavedMappingsHandler handler, CancellationToken ct) =>
            ToResult(await handler.UpdateAsync(user.UserId(), id, body, ct), created: false));

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, SavedMappingsHandler handler, CancellationToken ct) =>
            await handler.DeleteAsync(user.UserId(), id, ct) ? Results.NoContent() : NotFound());

        return app;
    }

    private static IResult ToResult(SaveMappingResult result, bool created) => result.Outcome switch
    {
        SaveMappingOutcome.Saved when created => Results.Created($"/api/import-mappings/{result.Mapping!.Id}", result.Mapping),
        SaveMappingOutcome.Saved => Results.Ok(result.Mapping),
        SaveMappingOutcome.Invalid => Results.ValidationProblem(result.Errors!),
        SaveMappingOutcome.NameTaken => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Name already used",
            detail: "You already have a mapping with this name. Choose another name."),
        SaveMappingOutcome.LimitReached => Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Too many mappings",
            detail: $"You can keep at most {SavedMappingsHandler.MaxPerUser} mappings. Delete one first."),
        _ => NotFound(),
    };

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Mapping not found", detail: "No saved mapping exists with that id.");
}

public static class UserExtensions
{
    /// <summary>The signed-in user's id, from the token's <c>sub</c> claim (every protected endpoint has one).</summary>
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : throw new InvalidOperationException("The token has no user id.");
}
