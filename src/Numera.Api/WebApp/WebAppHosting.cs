using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Numera.Api.WebApp;

/// <summary>
/// Serves the built Angular app from <c>wwwroot</c> when it is present (the production image), so the app and the
/// API share one origin: no CORS, one service to host. In development <c>wwwroot</c> does not exist and the Angular
/// dev server is used instead, so none of this is active.
/// </summary>
public static class WebAppHosting
{
    // Angular adds component styles as <style> elements, hence 'unsafe-inline' for styles only.
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    public static bool HasWebApp(this IWebHostEnvironment environment) =>
        !string.IsNullOrEmpty(environment.WebRootPath) && File.Exists(Path.Combine(environment.WebRootPath, "index.html"));

    /// <summary>Security headers for every response.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff"; // stops browsers guessing a different content type (CSV downloads)
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        await next(context);
    });

    /// <summary>
    /// Static files without sign-in (they hold no data). Build output with a content hash in its name never changes and
    /// is cached for a year; index.html is revalidated every time so a new release shows up at once.
    /// </summary>
    public static IApplicationBuilder UseWebAppFiles(this IApplicationBuilder app)
    {
        app.UseDefaultFiles();
        return app.UseStaticFiles(FileOptions);
    }

    // Shared by the static file middleware and the index.html fallback, so both set the same cache headers.
    private static readonly StaticFileOptions FileOptions = new()
    {
        ContentTypeProvider = new FileExtensionContentTypeProvider(),
        OnPrepareResponse = context =>
        {
            var name = context.File.Name;
            context.Context.Response.Headers[HeaderNames.CacheControl] =
                name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "no-cache"
                : IsHashed(name) ? "public, max-age=31536000, immutable"
                : "public, max-age=3600";
        },
    };

    /// <summary>
    /// App routes (/dashboard, /orders?...) load index.html and Angular takes over. Unknown /api paths stay a JSON 404,
    /// so a typo in an API call is never answered with an HTML page.
    /// </summary>
    public static void MapWebAppFallback(this IEndpointRouteBuilder app)
    {
        app.MapFallback("/api/{**path}", () => Results.Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "There is no API endpoint at this address."))
            .AllowAnonymous();
        app.MapFallbackToFile("index.html", FileOptions).AllowAnonymous();
    }

    // Angular's production build names files like main-ABCD1234.js and styles-ABCD1234.css.
    private static bool IsHashed(string fileName) =>
        System.Text.RegularExpressions.Regex.IsMatch(fileName, @"-[A-Z0-9]{8,}\.(js|css)$");
}
