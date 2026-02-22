# AzureChat

A simple .NET 8 console application that connects with **Azure OpenAI** and **Azure AI Search** to provide an interactive chatbot with optional **Retrieval-Augmented Generation (RAG)**, plus a **Blob → Cosmos DB ingestion pipeline** to populate the search index source data.

## Features

- Interactive console chat interface powered by [Spectre.Console](https://spectreconsole.net/)
- Azure OpenAI chat completions (GPT-4o or any deployed model)
- Azure AI Search document retrieval for RAG grounding
- Toggle RAG on/off at runtime without restarting
- Conversation history maintained for multi-turn dialogue
- Sources table displayed when RAG retrieves documents
- **Blob → Cosmos DB ingestion pipeline** — reads text files from Azure Blob Storage, chunks them, and upserts structured JSON documents into Azure Cosmos DB for Search indexing
- Full configuration via `appsettings.json` or environment variables

## Project structure

```
AzureChat.slnx
├── src/AzureChat/                      # Console application
│   ├── Configuration/                  # Options classes
│   │   ├── AzureOpenAIOptions.cs
│   │   ├── AzureSearchOptions.cs
│   │   ├── RagOptions.cs
│   │   ├── BlobStorageOptions.cs       # NEW
│   │   └── CosmosDbOptions.cs          # NEW
│   ├── Models/
│   │   ├── ChatMessage.cs
│   │   ├── SearchResult.cs
│   │   └── BlobDocument.cs             # NEW — Cosmos DB document schema
│   ├── Services/
│   │   ├── ChatService.cs / IChatService.cs
│   │   ├── SearchService.cs / ISearchService.cs
│   │   ├── RagService.cs / IRagService.cs
│   │   ├── BlobIngestionService.cs / IBlobIngestionService.cs   # NEW
│   │   ├── CosmosDbService.cs / ICosmosDbService.cs             # NEW
│   │   └── IngestionPipelineService.cs / IIngestionPipelineService.cs  # NEW
│   ├── ChatUi.cs                       # Interactive Spectre.Console UI loop
│   ├── Program.cs                      # DI wiring & entry point
│   └── appsettings.json                # Default configuration
└── tests/AzureChat.Tests/              # xUnit unit tests
    └── Services/
        ├── RagServiceTests.cs
        ├── IngestionPipelineServiceTests.cs    # NEW
        └── BlobIngestionServiceChunkingTests.cs # NEW
```

## Ingestion pipeline

The `/ingest` command reads every blob in the configured container, extracts its text, splits it into overlapping chunks, and upserts a `BlobDocument` record to Cosmos DB:

```
Azure Blob Storage
       │  BlobIngestionService
       │  • list blobs (optional prefix filter)
       │  • download content as UTF-8 text
       │  • split into overlapping chunks (configurable size)
       ▼
   BlobDocument (JSON)
   {
     "id": "<sha256-of-path>",
     "sourceBlob": "container/blob.txt",
     "title": "blob",
     "content": "<full text>",
     "chunks": [ { "chunkIndex": 0, "content": "..." }, … ],
     "metadata": { "contentType": "text/plain", "size": 1234, … },
     "ingestedAt": "2024-01-01T00:00:00Z"
   }
       │  CosmosDbService
       │  • create database / container if not exists
       │  • upsert (idempotent re-runs)
       ▼
Azure Cosmos DB  ──► Azure AI Search (index Cosmos as data source)
```

Azure AI Search can then index the Cosmos DB container as a data source, vectorise the `content` / `chunks` fields, and serve them back to the RAG chatbot.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- An **Azure OpenAI** resource with a deployed chat model
- An **Azure AI Search** resource with an index *(required when RAG is enabled)*
- An **Azure Blob Storage** account with a container of text documents *(required for `/ingest`)*
- An **Azure Cosmos DB** account *(required for `/ingest`)*

## Configuration

Edit `src/AzureChat/appsettings.json` or set the corresponding **environment variables** (use `__` as the section separator):

### Azure OpenAI

| Setting | Environment variable | Description |
|---|---|---|
| `AzureOpenAI:Endpoint` | `AzureOpenAI__Endpoint` | Azure OpenAI endpoint URL |
| `AzureOpenAI:ApiKey` | `AzureOpenAI__ApiKey` | Azure OpenAI API key |
| `AzureOpenAI:DeploymentName` | `AzureOpenAI__DeploymentName` | Deployed model name (default: `gpt-4o`) |
| `AzureOpenAI:MaxTokens` | `AzureOpenAI__MaxTokens` | Max tokens per response (default: `1024`) |
| `AzureOpenAI:Temperature` | `AzureOpenAI__Temperature` | Sampling temperature 0–2 (default: `0.7`) |

### Azure AI Search

| Setting | Environment variable | Description |
|---|---|---|
| `AzureSearch:Endpoint` | `AzureSearch__Endpoint` | Azure AI Search endpoint URL |
| `AzureSearch:ApiKey` | `AzureSearch__ApiKey` | Azure AI Search API key |
| `AzureSearch:IndexName` | `AzureSearch__IndexName` | Name of the search index |
| `AzureSearch:ContentField` | `AzureSearch__ContentField` | Index field holding text (default: `content`) |
| `AzureSearch:TitleField` | `AzureSearch__TitleField` | Index field holding the title (default: `title`) |
| `AzureSearch:TopK` | `AzureSearch__TopK` | Number of documents to retrieve (default: `3`) |
| `Rag:EnabledByDefault` | `Rag__EnabledByDefault` | Start with RAG on or off (default: `true`) |

### Azure Blob Storage (ingestion source)

| Setting | Environment variable | Description |
|---|---|---|
| `BlobStorage:ConnectionString` | `BlobStorage__ConnectionString` | Storage account connection string |
| `BlobStorage:AccountName` | `BlobStorage__AccountName` | Account name (if no connection string) |
| `BlobStorage:AccountKey` | `BlobStorage__AccountKey` | Account key (if no connection string) |
| `BlobStorage:ContainerName` | `BlobStorage__ContainerName` | Container to ingest from |
| `BlobStorage:BlobPrefix` | `BlobStorage__BlobPrefix` | Optional prefix filter (e.g. `docs/`) |
| `BlobStorage:ChunkSize` | `BlobStorage__ChunkSize` | Characters per chunk (default: `2000`) |
| `BlobStorage:ChunkOverlap` | `BlobStorage__ChunkOverlap` | Overlap between chunks (default: `200`) |

### Azure Cosmos DB (ingestion target)

| Setting | Environment variable | Description |
|---|---|---|
| `CosmosDb:Endpoint` | `CosmosDb__Endpoint` | Cosmos DB account endpoint |
| `CosmosDb:AccountKey` | `CosmosDb__AccountKey` | Cosmos DB account key |
| `CosmosDb:DatabaseName` | `CosmosDb__DatabaseName` | Database name |
| `CosmosDb:ContainerName` | `CosmosDb__ContainerName` | Container name |
| `CosmosDb:PartitionKeyPath` | `CosmosDb__PartitionKeyPath` | Partition key (default: `/sourceBlob`) |

## Running the application

```bash
cd src/AzureChat

# Chat + RAG
export AzureOpenAI__Endpoint="https://<your-resource>.openai.azure.com/"
export AzureOpenAI__ApiKey="<your-key>"
export AzureSearch__Endpoint="https://<your-resource>.search.windows.net"
export AzureSearch__ApiKey="<your-key>"
export AzureSearch__IndexName="<your-index>"

# Blob → Cosmos ingestion
export BlobStorage__ConnectionString="DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
export BlobStorage__ContainerName="documents"
export CosmosDb__Endpoint="https://<your-account>.documents.azure.com:443/"
export CosmosDb__AccountKey="<your-key>"
export CosmosDb__DatabaseName="rag"
export CosmosDb__ContainerName="documents"

dotnet run
```

## Chat commands

| Command | Description |
|---|---|
| `/rag on` | Enable RAG document retrieval |
| `/rag off` | Disable RAG (model knowledge only) |
| `/rag` | Show current RAG status |
| `/ingest` | Run Blob → Cosmos DB ingestion pipeline |
| `/clear` | Clear conversation history |
| `/config` | Show active configuration |
| `/help` | Show command list |
| `/exit` | Quit |

## Running the tests

```bash
dotnet test tests/AzureChat.Tests/
```
