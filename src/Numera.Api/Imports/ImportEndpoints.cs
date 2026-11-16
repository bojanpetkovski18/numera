using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Numera.Application.Imports;
using Numera.Infrastructure.Imports;

namespace Numera.Api.Imports;

public static class ImportEndpoints
{
    private const string FileField = "file";
    private const string MappingField = "mapping";

    /// <summary>A mapping names 11 columns of at most 100 characters; anything much longer is not a mapping.</summary>
    private const int MaxMappingJsonLength = 8 * 1024;

    /// <summary>The 10 MB file plus headroom for multipart headers and boundaries.</summary>
    public const long MaxUploadRequestBytes = ImportLimits.MaxFileBytes + 64 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
        group.MapPost("/", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadRequestBytes));
        group.MapPost("/preview", PreviewAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadRequestBytes));
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/errors.csv", ErrorReportAsync);

        return app;
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        ImportOrdersHandler handler,
        IHostApplicationLifetime lifetime,
        CancellationToken ct)
    {
        var (upload, error) = await ReadUploadAsync(request, ct);
        if (upload is null)
        {
            return error!;
        }

        ImportMapping? mapping = null;
        if (upload.Form.TryGetValue(MappingField, out var mappingValue) && !string.IsNullOrWhiteSpace(mappingValue.ToString()))
        {
            var errors = new Dictionary<string, string[]>();
            mapping = ParseMapping(mappingValue.ToString(), errors);
            if (mapping is null)
            {
                return Results.ValidationProblem(errors);
            }
        }

        await using var stream = upload.File.OpenReadStream();
        if (await LooksBinaryAsync(stream, ct))
        {
            return NotText();
        }

        // The file is fully received at this point. Processing is not tied to the request, so a client that
        // disconnects (closed tab, flaky network) does not roll back an import that is already running;
        // only application shutdown cancels it.
        var result = await handler.HandleAsync(stream, upload.FileName, mapping, lifetime.ApplicationStopping);

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

    /// <summary>
    /// The header and the first rows of a file, with a suggested mapping (or the user's saved one for files with the
    /// same headers). Same upload checks as an import; nothing is stored.
    /// </summary>
    private static async Task<IResult> PreviewAsync(
        HttpRequest request, ClaimsPrincipal user, SavedMappingsHandler mappings, CancellationToken ct)
    {
        var (upload, error) = await ReadUploadAsync(request, ct);
        if (upload is null)
        {
            return error!;
        }

        await using var stream = upload.File.OpenReadStream();
        if (await LooksBinaryAsync(stream, ct))
        {
            return NotText();
        }

        var sample = await OrderCsvReader.ReadSampleAsync(stream, ImportPreviewLimits.MaxRows, ct);
        if (sample is not { } s || s.Headers.All(string.IsNullOrEmpty))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "Nothing to preview", "The file is empty or its first row cannot be read.");
        }

        if (s.Headers.Count > ImportPreviewLimits.MaxColumns)
        {
            return Problem(
                StatusCodes.Status422UnprocessableEntity,
                "Too many columns",
                $"The file has {s.Headers.Count} columns; at most {ImportPreviewLimits.MaxColumns} are supported.");
        }

        var saved = await mappings.FindForHeadersAsync(user.UserId(), s.Headers, ct);
        return Results.Ok(ImportPreview.Create(s.Headers, s.Rows, saved));
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

    private sealed record CsvUpload(IFormCollection Form, IFormFile File, string FileName);

    /// <summary>
    /// Reads the multipart form and checks the file the way every CSV upload is checked: present, at most 10 MB,
    /// a .csv name and a CSV or plain-text content type. Returns the upload, or the problem response to send.
    /// </summary>
    private static async Task<(CsvUpload? Upload, IResult? Error)> ReadUploadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return (null, Problem(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported content type",
                $"Send the CSV as multipart/form-data in a field named '{FileField}'."));
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Kestrel's request body limit. Must come before the IOException clause: it derives from IOException.
            return (null, TooLarge());
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return (null, TooLarge());
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            // Malformed multipart (bad boundary, truncated body). An aborted upload also lands here; the client is
            // gone by then, so the response does not matter.
            return (null, Problem(
                StatusCodes.Status400BadRequest,
                "Malformed upload",
                "The multipart form data could not be read. Upload the file again."));
        }

        var file = form.Files.GetFile(FileField) ?? form.Files.FirstOrDefault();
        if (file is null)
        {
            return (null, Problem(
                StatusCodes.Status400BadRequest,
                "No file uploaded",
                $"Include the CSV file in a form field named '{FileField}'."));
        }

        if (file.Length > ImportLimits.MaxFileBytes)
        {
            return (null, TooLarge());
        }

        var fileName = Path.GetFileName(file.FileName);
        var isCsvName = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        if (!isCsvName || !AllowedContentTypes.Contains(file.ContentType ?? string.Empty))
        {
            return (null, Problem(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported file type",
                "Only .csv files with a CSV or plain-text content type can be imported."));
        }

        return (new CsvUpload(form, file, fileName), null);
    }

    /// <summary>The <c>mapping</c> form field: JSON in the shape of <see cref="ImportMappingInput"/>, validated.</summary>
    private static ImportMapping? ParseMapping(string json, Dictionary<string, string[]> errors)
    {
        if (json.Length > MaxMappingJsonLength)
        {
            errors[MappingField] = ["The mapping is too long."];
            return null;
        }

        ImportMappingInput? input;
        try
        {
            input = JsonSerializer.Deserialize<ImportMappingInput>(json, Json);
        }
        catch (JsonException)
        {
            errors[MappingField] = ["The mapping is not valid JSON."];
            return null;
        }

        return ImportMapping.TryCreate(input, errors);
    }

    private static IResult TooLarge() => Problem(
        StatusCodes.Status413PayloadTooLarge,
        "File too large",
        $"The file must be at most {ImportLimits.MaxFileBytes / (1024 * 1024)} MB.");

    private static IResult NotText() => Problem(
        StatusCodes.Status415UnsupportedMediaType,
        "Unsupported file type",
        "The file does not look like a text CSV.");

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
