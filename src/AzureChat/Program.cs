using System.Text.Json;
using System.Text.Json.Nodes;
using AzureChat.Configuration;
using AzureChat.Models;
using AzureChat.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuration — appsettings.json is loaded automatically by the Web SDK.
// appsettings.local.json (gitignored) holds credentials set via the in-app settings editor.
// Environment variable overrides use __ as the section separator:
//   AzureOpenAI__Endpoint, AzureOpenAI__ApiKey, BlobStorage__ConnectionString, etc.
builder.Configuration
    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Logging
builder.Logging.ClearProviders().AddConsole();

// Options
builder.Services
    .Configure<AzureOpenAIOptions>(builder.Configuration.GetSection(AzureOpenAIOptions.SectionName))
    .Configure<AzureSearchOptions>(builder.Configuration.GetSection(AzureSearchOptions.SectionName))
    .Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName))
    .Configure<BlobStorageOptions>(builder.Configuration.GetSection(BlobStorageOptions.SectionName))
    .Configure<CosmosDbOptions>(builder.Configuration.GetSection(CosmosDbOptions.SectionName));

// Application services
builder.Services
    .AddSingleton<ISearchService, SearchService>()
    .AddSingleton<IChatService, ChatService>()
    .AddSingleton<IRagService, RagService>()
    .AddSingleton<IBlobIngestionService, BlobIngestionService>()
    .AddSingleton<IBlobDownloadService, BlobDownloadService>()
    .AddSingleton<ICosmosDbService, CosmosDbService>()
    .AddSingleton<IIngestionPipelineService, IngestionPipelineService>();

// Consistent camelCase JSON for API input and output
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

// Serve wwwroot/index.html for / and any unmatched paths
app.UseDefaultFiles();
app.UseStaticFiles();

// ── API endpoints ─────────────────────────────────────────────────────────────

// GET /api/config — current configuration (API keys are never returned)
app.MapGet("/api/config", (
    IOptions<AzureOpenAIOptions> openAI,
    IOptions<AzureSearchOptions> search,
    IOptions<BlobStorageOptions> blob,
    IOptions<CosmosDbOptions> cosmos,
    IRagService rag) =>
{
    return Results.Ok(new
    {
        openAI = new
        {
            endpoint = openAI.Value.Endpoint,
            deploymentName = openAI.Value.DeploymentName,
            configured = !string.IsNullOrWhiteSpace(openAI.Value.Endpoint)
                      && !string.IsNullOrWhiteSpace(openAI.Value.ApiKey),
        },
        search = new
        {
            endpoint = search.Value.Endpoint,
            indexName = search.Value.IndexName,
            keyField = search.Value.KeyField,
            contentField = search.Value.ContentField,
            titleField = search.Value.TitleField,
            configured = !string.IsNullOrWhiteSpace(search.Value.Endpoint)
                      && !string.IsNullOrWhiteSpace(search.Value.ApiKey),
        },
        rag = new { enabled = rag.IsEnabled },
        blob = new
        {
            accountName = blob.Value.AccountName,
            containerName = blob.Value.ContainerName,
            configured = !string.IsNullOrWhiteSpace(blob.Value.ContainerName)
                      && (!string.IsNullOrWhiteSpace(blob.Value.ConnectionString)
                          || !string.IsNullOrWhiteSpace(blob.Value.AccountName)),
        },
        cosmos = new
        {
            endpoint = cosmos.Value.Endpoint,
            databaseName = cosmos.Value.DatabaseName,
            containerName = cosmos.Value.ContainerName,
            configured = !string.IsNullOrWhiteSpace(cosmos.Value.Endpoint)
                      && !string.IsNullOrWhiteSpace(cosmos.Value.AccountKey),
        },
    });
});

// GET /api/rag — RAG toggle status
app.MapGet("/api/rag", (IRagService rag) =>
    Results.Ok(new { enabled = rag.IsEnabled }));

// POST /api/settings — save credentials to appsettings.local.json and reload config
app.MapPost("/api/settings", async (
    SettingsUpdateRequest req,
    IConfiguration configuration,
    IWebHostEnvironment env,
    ILogger<Program> logger) =>
{
    var localSettingsPath = Path.Combine(env.ContentRootPath, "appsettings.local.json");

    // Load existing local settings (if any) so we only overwrite supplied sections.
    JsonObject root;
    if (File.Exists(localSettingsPath))
    {
        try
        {
            root = JsonNode.Parse(await File.ReadAllTextAsync(localSettingsPath)) as JsonObject
                   ?? new JsonObject();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not parse existing appsettings.local.json — starting fresh");
            root = new JsonObject();
        }
    }
    else
    {
        root = new JsonObject();
    }

    static void Set(JsonObject obj, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) obj[key] = JsonValue.Create(value);
    }

    if (req.OpenAI is { } openAI)
    {
        var s = new JsonObject();
        Set(s, "Endpoint", openAI.Endpoint);
        Set(s, "ApiKey", openAI.ApiKey);
        Set(s, "DeploymentName", openAI.DeploymentName);
        root["AzureOpenAI"] = s;
    }

    if (req.Search is { } search)
    {
        var s = new JsonObject();
        Set(s, "Endpoint", search.Endpoint);
        Set(s, "ApiKey", search.ApiKey);
        Set(s, "IndexName", search.IndexName);
        Set(s, "ContentField", search.ContentField);
        Set(s, "TitleField", search.TitleField);
        Set(s, "KeyField", search.KeyField);
        Set(s, "VectorField", search.VectorField);
        root["AzureSearch"] = s;
    }

    if (req.Blob is { } blob)
    {
        var s = new JsonObject();
        Set(s, "ConnectionString", blob.ConnectionString);
        Set(s, "AccountName", blob.AccountName);
        Set(s, "AccountKey", blob.AccountKey);
        Set(s, "ContainerName", blob.ContainerName);
        root["BlobStorage"] = s;
    }

    if (req.Cosmos is { } cosmos)
    {
        var s = new JsonObject();
        Set(s, "Endpoint", cosmos.Endpoint);
        Set(s, "AccountKey", cosmos.AccountKey);
        Set(s, "DatabaseName", cosmos.DatabaseName);
        Set(s, "ContainerName", cosmos.ContainerName);
        Set(s, "PartitionKeyPath", cosmos.PartitionKeyPath);
        Set(s, "PartitionKeyValue", cosmos.PartitionKeyValue);
        root["CosmosDb"] = s;
    }

    try
    {
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(localSettingsPath, json);
        // Reload in-process config — IOptionsMonitor.CurrentValue is updated immediately.
        if (configuration is IConfigurationRoot configRoot)
            configRoot.Reload();
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Could not write appsettings.local.json",
            statusCode: 500);
    }

    return Results.Ok(new
    {
        message = "Settings saved. Chat and Search pick up the new values immediately. " +
                  "Blob Storage and Cosmos DB require a server restart."
    });
});

// POST /api/rag — enable or disable RAG
app.MapPost("/api/rag", (SetRagRequest req, IRagService rag) =>
{
    rag.IsEnabled = req.Enabled;
    return Results.Ok(new { enabled = rag.IsEnabled });
});

// POST /api/chat — send a message, get an AI response (with optional RAG)
app.MapPost("/api/chat", async (ChatApiRequest req, IRagService rag, CancellationToken ct) =>
{
    var history = (req.History ?? [])
        .Select(m => new ChatMessage(m.Role, m.Content))
        .ToList<ChatMessage>();

    try
    {
        RagResponse response = await rag.AskAsync(req.Message, history, ct);
        return Results.Ok(new
        {
            answer = response.Answer,
            sources = response.Sources
                .Select(s => new { s.Title, s.Score, sourcePath = s.SourcePath })
                .ToArray(),
        });
    }
    catch (Exception ex)
    {
        // Distinguish misconfiguration errors (4xx) from true server failures (5xx).
        // AzureDeploymentNotFoundException is a user-fixable config problem → 400 Bad Request.
        if (ex is AzureDeploymentNotFoundException dnf)
        {
            return Results.Problem(
                detail: dnf.Message,
                title: $"Deployment '{dnf.DeploymentName}' not found — update DeploymentName in appsettings.json",
                statusCode: 400);
        }

        return Results.Problem(detail: ex.Message, title: "Chat request failed", statusCode: 500);
    }
});

// GET /api/search/fields — introspect the live index and return field list with suggestions
app.MapGet("/api/search/fields", async (ISearchService search, CancellationToken ct) =>
{
    try
    {
        IReadOnlyList<IndexField> fields = await search.GetIndexFieldsAsync(ct);
        return Results.Ok(new
        {
            fields = fields.Select(f => new
            {
                name          = f.Name,
                type          = f.Type,
                isSearchable  = f.IsSearchable,
                isRetrievable = f.IsRetrievable,
                suggestedRole = f.SuggestedRole,
            }),
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Could not retrieve index fields",
            statusCode: 500);
    }
});

// GET /api/blob/download?path={sourcePath} — generate a SAS URL and redirect to it
app.MapGet("/api/blob/download", (
    string? path,
    IBlobDownloadService blobDownload) =>
{
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest("path parameter is required");

    string? url = blobDownload.GenerateDownloadUrl(path);
    if (url is null)
        return Results.NotFound("Could not resolve a download URL for the given path.");

    // 302 redirect → browser opens / downloads the file directly from Blob Storage.
    return Results.Redirect(url);
});

// POST /api/ingest — run the Blob → Cosmos DB ingestion pipeline
app.MapPost("/api/ingest", async (IIngestionPipelineService pipeline, CancellationToken ct) =>
{
    try
    {
        IngestionSummary summary = await pipeline.RunAsync(ct);
        return Results.Ok(new
        {
            succeeded = summary.Succeeded,
            failed = summary.Failed,
            errors = summary.Errors,
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Ingestion pipeline failed", statusCode: 500);
    }
});

// Bind on all interfaces so GitHub Codespaces port-forwarding can reach the server
app.Run("http://+:8080");

// ── Request / Response DTOs ───────────────────────────────────────────────────
record SetRagRequest(bool Enabled);
record ChatHistoryItem(string Role, string Content);
record ChatApiRequest(string Message, IReadOnlyList<ChatHistoryItem>? History);

// Settings update DTOs — all fields nullable; only non-empty values are written to appsettings.local.json.
record OpenAISettingsUpdate(string? Endpoint, string? ApiKey, string? DeploymentName);
record SearchSettingsUpdate(
    string? Endpoint, string? ApiKey, string? IndexName,
    string? ContentField, string? TitleField, string? KeyField, string? VectorField);
record BlobSettingsUpdate(
    string? ConnectionString, string? AccountName, string? AccountKey, string? ContainerName);
record CosmosSettingsUpdate(
    string? Endpoint, string? AccountKey,
    string? DatabaseName, string? ContainerName,
    string? PartitionKeyPath, string? PartitionKeyValue);
record SettingsUpdateRequest(
    OpenAISettingsUpdate? OpenAI,
    SearchSettingsUpdate? Search,
    BlobSettingsUpdate? Blob,
    CosmosSettingsUpdate? Cosmos);

