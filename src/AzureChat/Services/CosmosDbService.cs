using System.Net;
using System.Text.Json;
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

        // Cosmos SDK uses Newtonsoft by default; we pass the document as a JObject-equivalent
        // by serialising via System.Text.Json and deserialising as dynamic to avoid a hard
        // dependency on Newtonsoft in this layer.
        string json = JsonSerializer.Serialize(document);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        ResponseMessage response = await container.UpsertItemStreamAsync(
            stream,
            new PartitionKey(document.SourceBlob),
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string body = response.Content is not null
                ? await new StreamReader(response.Content).ReadToEndAsync(cancellationToken)
                : "(no body)";
            throw new InvalidOperationException(
                $"Cosmos upsert failed ({(int)response.StatusCode}): {body}");
        }

        _logger.LogDebug(
            "Upserted document '{Id}' ({SourceBlob}) into Cosmos DB container '{Container}'",
            document.Id, document.SourceBlob, _options.ContainerName);
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
