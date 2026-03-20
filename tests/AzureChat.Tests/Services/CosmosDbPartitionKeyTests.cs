using System.Text.Json;
using System.Text.Json.Nodes;
using AzureChat.Configuration;
using AzureChat.Models;
using AzureChat.Models;
using Xunit;

namespace AzureChat.Tests.Services;

/// <summary>
/// Tests for the partition key field/value logic in CosmosDbOptions and the document
/// JSON injection behaviour that CosmosDbService relies on.
/// These run without any Azure SDK calls.
/// </summary>
public class CosmosDbPartitionKeyTests
{
    // ── CosmosDbOptions.PartitionKeyField ─────────────────────────────────────

    [Fact]
    public void PartitionKeyField_DefaultPath_ReturnsSourceBlob()
    {
        var opts = new CosmosDbOptions();  // default PartitionKeyPath = "/sourceBlob"
        Assert.Equal("sourceBlob", opts.PartitionKeyField);
    }

    [Fact]
    public void PartitionKeyField_VendorIdPath_ReturnsVendorId()
    {
        var opts = new CosmosDbOptions { PartitionKeyPath = "/vendorId" };
        Assert.Equal("vendorId", opts.PartitionKeyField);
    }

    [Fact]
    public void PartitionKeyField_NestedPath_StripsSingleLeadingSlash()
    {
        var opts = new CosmosDbOptions { PartitionKeyPath = "/org/tenant" };
        Assert.Equal("org/tenant", opts.PartitionKeyField);
    }

    [Fact]
    public void PartitionKeyField_EmptyPath_FallsBackToSourceBlob()
    {
        var opts = new CosmosDbOptions { PartitionKeyPath = "" };
        Assert.Equal("sourceBlob", opts.PartitionKeyField);
    }

    // ── Document JSON injection simulation ───────────────────────────────────
    // We test the same logic used in CosmosDbService.UpsertDocumentAsync to ensure
    // the injected field appears correctly in the serialised JSON.

    private static BlobDocument MakeDoc(string blobName = "container/test.txt") =>
        new(
            id: "abc123",
            sourceBlob: blobName,
            title: "Test",
            content: "Hello world",
            chunks: [new DocumentChunk(0, "Hello world")],
            metadata: new BlobMetadata("container", "test.txt", "text/plain", 11, DateTimeOffset.UtcNow),
            ingestedAt: DateTimeOffset.UtcNow);

    private static (string pkValue, JsonObject jsonObj) SimulateInject(
        CosmosDbOptions opts, BlobDocument doc)
    {
        string pkValue = string.IsNullOrWhiteSpace(opts.PartitionKeyValue)
            ? doc.SourceBlob
            : opts.PartitionKeyValue;

        // Mirror the production logic: deserialise directly to JsonObject (not via JsonDocument)
        // to ensure all values are owned copies.
        string rawJson = JsonSerializer.Serialize(doc);
        var jsonObj = JsonSerializer.Deserialize<JsonNode>(rawJson)!.AsObject();
        jsonObj[opts.PartitionKeyField] = pkValue;
        return (pkValue, jsonObj);
    }

    [Fact]
    public void Inject_DefaultConfig_SourceBlobFieldHasDocumentValue()
    {
        var opts = new CosmosDbOptions();  // default: path=/sourceBlob, value=""
        var doc  = MakeDoc("container/my-doc.txt");

        var (pkValue, jsonObj) = SimulateInject(opts, doc);

        Assert.Equal("container/my-doc.txt", pkValue);
        Assert.Equal("container/my-doc.txt", jsonObj["sourceBlob"]!.GetValue<string>());
    }

    [Fact]
    public void Inject_StaticPartitionKeyValue_InjectsVendorIdField()
    {
        var opts = new CosmosDbOptions
        {
            PartitionKeyPath  = "/vendorId",
            PartitionKeyValue = "acme-corp",
        };
        var doc = MakeDoc();

        var (pkValue, jsonObj) = SimulateInject(opts, doc);

        Assert.Equal("acme-corp", pkValue);
        Assert.Equal("acme-corp", jsonObj["vendorId"]!.GetValue<string>());
    }

    [Fact]
    public void Inject_StaticPartitionKeyValue_DoesNotAlterOtherFields()
    {
        var opts = new CosmosDbOptions
        {
            PartitionKeyPath  = "/vendorId",
            PartitionKeyValue = "org1",
        };
        var doc = MakeDoc("container/report.pdf");

        var (_, jsonObj) = SimulateInject(opts, doc);

        // Original fields must be preserved
        Assert.Equal("container/report.pdf", jsonObj["sourceBlob"]!.GetValue<string>());
        Assert.Equal("abc123",               jsonObj["id"]!.GetValue<string>());
        Assert.Equal("Test",                 jsonObj["title"]!.GetValue<string>());
    }

    [Fact]
    public void Inject_EmptyPartitionKeyValue_UsesSourceBlobAsPkValue()
    {
        var opts = new CosmosDbOptions
        {
            PartitionKeyPath  = "/vendorId",
            PartitionKeyValue = "",        // empty → auto-derive
        };
        var doc = MakeDoc("container/file.txt");

        var (pkValue, jsonObj) = SimulateInject(opts, doc);

        // When PartitionKeyValue is empty, falls back to SourceBlob
        Assert.Equal("container/file.txt", pkValue);
        Assert.Equal("container/file.txt", jsonObj["vendorId"]!.GetValue<string>());
    }

    [Fact]
    public void Inject_SourceBlobPath_OverwritesExistingSourceBlobField()
    {
        // Default config: the document already has a "sourceBlob" field from serialisation.
        // The inject step should overwrite it with the same value (idempotent).
        var opts = new CosmosDbOptions();  // default
        var doc  = MakeDoc("container/doc.txt");

        var (pkValue, jsonObj) = SimulateInject(opts, doc);

        string roundTripped = JsonSerializer.Deserialize<JsonElement>(jsonObj.ToJsonString())
            .GetProperty("sourceBlob").GetString()!;

        Assert.Equal("container/doc.txt", roundTripped);
        Assert.Equal("container/doc.txt", pkValue);
    }
}
