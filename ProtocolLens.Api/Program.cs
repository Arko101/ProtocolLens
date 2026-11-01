using ProtocolLens.Api.Models;
using ProtocolLens.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
//Adding CORS policy to allow requests from Angular frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    }
        );
});

var app = builder.Build();
app.UseCors("AngularPolicy");


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Local-only, single-user processing limits (Stage 4 defaults).
const long MaxUploadBytes = 50L * 1024 * 1024; // 50 MiB
const long MaxTotalFrames = 100_000;
const long MaxDhcpObservations = 10_000;
var analysisTimeout = TimeSpan.FromSeconds(30);

// One concurrent analysis at a time (local single-user tool - Stage 4 default).
using var analysisGate = new SemaphoreSlim(1, 1);

app.MapGet("/api/status", () =>
{
    return new
    {
        message = "ProtocolLens API is running"
    };
});

app.MapPost("/api/pcap", async (IFormFile? file, CancellationToken requestAborted) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.Problem(
            title: "No file provided",
            detail: "Upload must include a non-empty 'file' multipart field containing a PCAP/PCAPNG capture.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    if (file.Length > MaxUploadBytes)
    {
        return Results.Problem(
            title: "File too large",
            detail: $"The uploaded file exceeds the configured limit of {MaxUploadBytes / (1024 * 1024)} MiB.",
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    if (!await analysisGate.WaitAsync(TimeSpan.Zero, requestAborted))
    {
        return Results.Problem(
            title: "Server busy",
            detail: "Another analysis is already in progress. This local tool processes one capture at a time.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // Application-generated temporary filename; never derived from client-supplied input.
    var tmpFilePath = Path.Combine(Path.GetTempPath(), $"protocollens-{Guid.NewGuid():N}.pcap");

    try
    {
        using (var tempStream = new FileStream(tmpFilePath, FileMode.Create))
        {
            await file.CopyToAsync(tempStream, requestAborted);
        }

        using var deadline = new CancellationTokenSource(analysisTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, deadline.Token);

        var coordinator = new AnalysisCoordinator();

        AnalysisResponse response;
        try
        {
            response = await Task.Run(() =>
                coordinator.Analyze(tmpFilePath, file.FileName, file.Length, MaxTotalFrames, MaxDhcpObservations, linked.Token),
                linked.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // Cooperative deadline only - a blocking native read cannot be hard-interrupted
            // mid-call; this bounds subsequent managed processing.
            return Results.Problem(
                title: "Analysis deadline exceeded",
                detail: $"Analysis did not complete within the configured {analysisTimeout.TotalSeconds:0}s cooperative deadline.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(499); // Non-standard but widely used "client closed request" code.
        }

        return Results.Ok(response);
    }
    catch (UnauthorizedAccessException)
    {
        // Missing native prerequisite (e.g. no libpcap/Npcap) typically surfaces as an access
        // or platform-invoke failure, not a malformed-input error.
        return Results.Problem(
            title: "Capture engine unavailable",
            detail: "The native packet-capture library required to read this file is not available on this server.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception)
    {
        // Internal exceptions are logged server-side only; never expose exception
        // messages, paths, or capture contents to the client.
        app.Logger.LogError("Unhandled exception while analyzing an uploaded capture.");
        return Results.Problem(
            title: "Analysis failed",
            detail: "An unexpected error occurred while analyzing the capture.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
    finally
    {
        try
        {
            if (File.Exists(tmpFilePath))
            {
                File.Delete(tmpFilePath);
            }
        }
        catch
        {
            // A cleanup failure must never override the primary response/error already produced.
        }

        analysisGate.Release();
    }
}).DisableAntiforgery();

app.Run();



