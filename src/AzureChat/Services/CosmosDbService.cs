using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AzureChat.Configuration;
using AzureChat.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureChat.Services;

/// <summary>
/// Upserts <see cref="BlobDocument"/> records into Azure Cosmos DB.
/// The database and container are created on first use if they do not exist.
/// </summary>
public sealed class CosmosDbService : ICosmosDbService, IAsyncDisposable
{
    private readonly CosmosClient _cosmosClient;
    private readonly CosmosDbOptions _options;
    private readonly ILogger<CosmosDbService> _logger;

    // Lazily initialised container reference (created on first upsert).
    private Container? _container;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public CosmosDbService(
        IOptions<CosmosDbOptions> options,
        ILogger<CosmosDbService> logger)
    {
        _options = options.Value;
        _logger = logger;

        _cosmosClient = new CosmosClient(
            _options.Endpoint,
            _options.AccountKey,
            new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                },
            });
    }

    /// <inheritdoc/>
    public async Task UpsertDocumentAsync(
        BlobDocument document,
        CancellationToken cancellationToken = default)
    {
        Container container = await EnsureContainerAsync(cancellationToken);

        // Determine the partition key value to use for this document.
        // Priority: 1) explicit PartitionKeyValue from config (static, same for all docs)
        //           2) field derived from PartitionKeyPath extracted from the document JSON
        //           3) fallback: document.SourceBlob
        string pkField = _options.PartitionKeyField;          // e.g. "vendorId", "sourceBlob"
        string pkValue = string.IsNullOrWhiteSpace(_options.PartitionKeyValue)
            ? document.SourceBlob                             // default / auto-derive
            : _options.PartitionKeyValue;                     // static value from config

        // Serialise document as a mutable JsonObject so we can inject the PK field if it is
        // missing or if a static override is configured — Cosmos requires the PK field to exist
        // in the document JSON with the exact value passed to UpsertItemStreamAsync.
        // Note: deserialise directly to JsonObject (not via JsonDocument) so all values are owned
        // copies and there is no risk of ObjectDisposedException from lazy JsonElement wrappers.
        string rawJson = JsonSerializer.Serialize(document);
        var jsonObj = JsonSerializer.Deserialize<JsonNode>(rawJson)!.AsObject();

        // Inject/overwrite the PK field with the resolved value.
        jsonObj[pkField] = pkValue;

        string json = jsonObj.ToJsonString();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        ResponseMessage response;
        try
        {
            response = await container.UpsertItemStreamAsync(
                stream,
                new PartitionKey(pkValue),
                cancellationToken: cancellationToken);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("partition key path") ||
                                            ex.Message.Contains("PartitionKey"))
        {
            throw new InvalidOperationException(
                $"Cosmos partition key mismatch: the container '{_options.ContainerName}' " +
                $"uses a different partition key path than '{_options.PartitionKeyPath}'. " +
                $"Set CosmosDb__PartitionKeyPath in appsettings.json to match your container's " +
                $"actual partition key path, and optionally set CosmosDb__PartitionKeyValue to a " +
                $"static string (e.g. \"rag-docs\"). Original: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            string body = "(no response body)";
            if (response.Content is not null)
            {
                try
                {
                    if (response.Content.CanSeek)
                        response.Content.Position = 0;
                    body = await new StreamReader(response.Content).ReadToEndAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not read Cosmos error response body for status {StatusCode}.", (int)response.StatusCode);
                }
            }

            if (string.IsNullOrWhiteSpace(body))
                body = response.ErrorMessage ?? "(no response body)";

            // Detect partition key mismatch in the response body and add guidance.
            if (body.Contains("partition key path") || body.Contains("PartitionKey"))
            {
                body += $" → Fix: set CosmosDb__PartitionKeyPath and CosmosDb__PartitionKeyValue " +
                        $"in appsettings.json to match your existing container's partition key. " +
                        $"Current config: path='{_options.PartitionKeyPath}', value='{_options.PartitionKeyValue}'.";
            }

            throw new InvalidOperationException(
                $"Cosmos upsert failed ({(int)response.StatusCode}): {body}");
        }

        _logger.LogDebug(
            "Upserted document '{Id}' ({SourceBlob}) into Cosmos DB container '{Container}' [pk={PkField}={PkValue}]",
            document.Id, document.SourceBlob, _options.ContainerName, pkField, pkValue);
    }

    public async ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        _cosmosClient.Dispose();
        await ValueTask.CompletedTask;
    }

    // -----------------------------------------------------------------------

    private async Task<Container> EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_container is not null)
            return _container;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_container is not null)
                return _container;

            DatabaseResponse dbResponse = await _cosmosClient
                .CreateDatabaseIfNotExistsAsync(_options.DatabaseName, cancellationToken: cancellationToken);

            ContainerResponse containerResponse = await dbResponse.Database
                .CreateContainerIfNotExistsAsync(
                    _options.ContainerName,
                    _options.PartitionKeyPath,
                    cancellationToken: cancellationToken);

            _container = containerResponse.Container;

            _logger.LogInformation(
                "Cosmos DB ready — database: '{Db}', container: '{Container}'",
                _options.DatabaseName, _options.ContainerName);

            return _container;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
