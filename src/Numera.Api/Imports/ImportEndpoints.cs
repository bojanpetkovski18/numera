using Numera.Application.Imports;
using Numera.Infrastructure.Imports;

namespace Numera.Api.Imports;

public static class ImportEndpoints
{
    private const string FileField = "file";

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/csv",
        "application/csv",
        "text/comma-separated-values",
        "application/vnd.ms-excel", // what Windows browsers send for .csv
        "text/plain",
        "application/octet-stream",
    };

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports");

        // Antiforgery is for cookie-authenticated browsers; this API authenticates with a bearer token.
        group.MapPost("/", UploadAsync).DisableAntiforgery();
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/errors.csv", ErrorReportAsync);

        return app;
    }

    private static async Task<IResult> UploadAsync(HttpRequest request, ImportOrdersHandler handler, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Problem(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported content type",
                $"Send the CSV as multipart/form-data in a field named '{FileField}'.");
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (InvalidDataException)
        {
            return TooLarge();
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }

        var file = form.Files.GetFile(FileField) ?? form.Files.FirstOrDefault();
        if (file is null)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "No file uploaded",
                $"Include the CSV file in a form field named '{FileField}'.");
        }

        if (file.Length > ImportLimits.MaxFileBytes)
        {
            return TooLarge();
        }

        var fileName = Path.GetFileName(file.FileName);
        var isCsvName = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        if (!isCsvName || !AllowedContentTypes.Contains(file.ContentType ?? string.Empty))
        {
            return Problem(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported file type",
                "Only .csv files with a CSV or plain-text content type can be imported.");
        }

        await using var stream = file.OpenReadStream();
        if (await LooksBinaryAsync(stream, ct))
        {
            return Problem(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported file type",
                "The file does not look like a text CSV.");
        }

        var result = await handler.HandleAsync(stream, fileName, ct);

        if (result.Rejection is { } rejection)
        {
            var status = rejection.Kind == ImportRejectionKind.TooManyRows
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status422UnprocessableEntity;
            return Results.Problem(
                statusCode: status,
                title: "Import rejected",
                detail: rejection.Message,
                extensions: new Dictionary<string, object?> { ["importId"] = result.Job.Id });
        }

        return Results.Created($"/api/imports/{result.Job.Id}", result.Job);
    }

    private static async Task<IResult> GetAsync(Guid id, GetImportJobHandler handler, CancellationToken ct)
    {
        var job = await handler.HandleAsync(id, ct);
        return job is null
            ? Problem(StatusCodes.Status404NotFound, "Import not found", "No import exists with that id.")
            : Results.Ok(job);
    }

    private static async Task<IResult> ListAsync(ListImportJobsHandler handler, CancellationToken ct, int limit = ListImportJobsHandler.DefaultLimit) =>
        Results.Ok(await handler.HandleAsync(limit, ct));

    private static async Task<IResult> ErrorReportAsync(Guid id, GetImportErrorsHandler handler, CancellationToken ct)
    {
        if (!await handler.JobExistsAsync(id, ct))
        {
            return Problem(StatusCodes.Status404NotFound, "Import not found", "No import exists with that id.");
        }

        // Streamed straight from the database; a UTF-8 BOM lets Excel read non-ASCII text correctly.
        return Results.Stream(
            async stream =>
            {
                await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true), leaveOpen: true);
                await ImportErrorReport.WriteAsync(handler.StreamAsync(id), writer, ct);
            },
            contentType: "text/csv; charset=utf-8",
            fileDownloadName: $"import-{id}-errors.csv");
    }

    private static IResult TooLarge() => Problem(
        StatusCodes.Status413PayloadTooLarge,
        "File too large",
        $"The file must be at most {ImportLimits.MaxFileBytes / (1024 * 1024)} MB.");

    private static IResult Problem(int status, string title, string detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail);

    /// <summary>Text files never contain NUL bytes; spreadsheets, images and archives usually do early on.</summary>
    private static async Task<bool> LooksBinaryAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        stream.Position = 0;
        return buffer.AsSpan(0, read).Contains((byte)0);
    }
}