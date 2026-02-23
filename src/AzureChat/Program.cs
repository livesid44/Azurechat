using System.Text.Json;
using AzureChat.Configuration;
using AzureChat.Models;
using AzureChat.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuration — appsettings.json is loaded automatically by the Web SDK.
// Environment variable overrides use __ as the section separator:
//   AzureOpenAI__Endpoint, AzureOpenAI__ApiKey, BlobStorage__ConnectionString, etc.
builder.Configuration.AddEnvironmentVariables();

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
            configured = !string.IsNullOrWhiteSpace(search.Value.Endpoint)
                      && !string.IsNullOrWhiteSpace(search.Value.ApiKey),
        },
        rag = new { enabled = rag.IsEnabled },
        blob = new
        {
            containerName = blob.Value.ContainerName,
            configured = !string.IsNullOrWhiteSpace(blob.Value.ContainerName)
                      && (!string.IsNullOrWhiteSpace(blob.Value.ConnectionString)
                          || !string.IsNullOrWhiteSpace(blob.Value.AccountName)),
        },
        cosmos = new
        {
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
                .Select(s => new { s.Title, s.Score })
                .ToArray(),
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Chat request failed", statusCode: 500);
    }
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

