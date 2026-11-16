using System.Security.Claims;
using Numera.Api.Imports;
using Numera.Application.Views;

namespace Numera.Api.Views;

/// <summary>A signed-in user's saved views and the share links to them. Another user's view or link is always "not found".</summary>
public static class ViewEndpoints
{
    public static IEndpointRouteBuilder MapViewEndpoints(this IEndpointRouteBuilder app)
    {
        var views = app.MapGroup("/api/views");
        views.MapGet("/", async (ClaimsPrincipal user, SavedViewsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListAsync(user.UserId(), ct)));
        views.MapPost("/", async (SaveViewRequest body, ClaimsPrincipal user, SavedViewsHandler handler, CancellationToken ct) =>
            ToResult(await handler.CreateAsync(user.UserId(), body, ct), v => Results.Created($"/api/views/{v.Id}", v)));
        views.MapPut("/{id:guid}", async (Guid id, SaveViewRequest body, ClaimsPrincipal user, SavedViewsHandler handler, CancellationToken ct) =>
            ToResult(await handler.UpdateAsync(user.UserId(), id, body, ct), Results.Ok));
        views.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, SavedViewsHandler handler, CancellationToken ct) =>
            await handler.DeleteAsync(user.UserId(), id, ct) ? Results.NoContent() : NotFound("View"));

        views.MapPost("/{id:guid}/shares", async (Guid id, CreateShareRequest body, ClaimsPrincipal user, ShareLinksHandler handler, CancellationToken ct) =>
            ToResult(await handler.CreateAsync(user.UserId(), id, body, ct), created =>
            {
                // The token is in this response only; it must not be cached anywhere on the way.
                return Results.Created($"/api/shares/{created.Link.Id}", new { created.Link, created.Token, url = $"/shared/{created.Token}" });
            }));

        var shares = app.MapGroup("/api/shares");
        shares.MapGet("/", async (ClaimsPrincipal user, ShareLinksHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListActiveAsync(user.UserId(), ct)));
        shares.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ShareLinksHandler handler, CancellationToken ct) =>
            await handler.RevokeAsync(user.UserId(), id, ct) ? Results.NoContent() : NotFound("Link"));

        views.AddEndpointFilter(NoStore);
        shares.AddEndpointFilter(NoStore);
        return app;
    }

    private static async ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return await next(context);
    }

    private static IResult ToResult<T>(ViewResult<T> result, Func<T, IResult> done) => result.Outcome switch
    {
        ViewOutcome.Done => done(result.Value!),
        ViewOutcome.Invalid => Results.ValidationProblem(result.Errors!),
        ViewOutcome.NameTaken => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Name already used",
            detail: "You already have a view with this name. Choose another name."),
        ViewOutcome.LimitReached => Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Limit reached",
            detail: typeof(T) == typeof(CreatedShareLink)
                ? $"A view can have at most {ShareLinksHandler.MaxActivePerView} active links. Revoke one first."
                : $"You can keep at most {SavedViewsHandler.MaxPerUser} views. Delete one first."),
        _ => NotFound("View"),
    };

    private static IResult NotFound(string what) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: $"{what} not found", detail: $"No {what.ToLowerInvariant()} exists with that id.");
}
