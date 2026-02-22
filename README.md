# AzureChat

A simple .NET 8 console application that connects with **Azure OpenAI** and **Azure AI Search** to provide an interactive chatbot with optional **Retrieval-Augmented Generation (RAG)**.

## Features

- Interactive console chat interface powered by [Spectre.Console](https://spectreconsole.net/)
- Azure OpenAI chat completions (GPT-4o or any deployed model)
- Azure AI Search document retrieval for RAG grounding
- Toggle RAG on/off at runtime without restarting
- Conversation history maintained for multi-turn dialogue
- Sources table displayed when RAG retrieves documents
- Full configuration via `appsettings.json` or environment variables

## Project structure

```
AzureChat.sln
├── src/AzureChat/           # Console application
│   ├── Configuration/       # Options classes (AzureOpenAI, AzureSearch, Rag)
│   ├── Models/              # ChatMessage, SearchResult
│   ├── Services/            # ChatService, SearchService, RagService (+ interfaces)
│   ├── ChatUi.cs            # Interactive Spectre.Console UI loop
│   ├── Program.cs           # DI wiring & entry point
│   └── appsettings.json     # Default configuration
└── tests/AzureChat.Tests/   # xUnit unit tests
    └── Services/
        └── RagServiceTests.cs
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- An **Azure OpenAI** resource with a deployed chat model
- An **Azure AI Search** resource with an index containing your documents *(required only when RAG is enabled)*

## Configuration

Edit `src/AzureChat/appsettings.json` or set the corresponding **environment variables** (use `__` as the section separator):

| Setting | Environment variable | Description |
|---|---|---|
| `AzureOpenAI:Endpoint` | `AzureOpenAI__Endpoint` | Azure OpenAI endpoint URL |
| `AzureOpenAI:ApiKey` | `AzureOpenAI__ApiKey` | Azure OpenAI API key |
| `AzureOpenAI:DeploymentName` | `AzureOpenAI__DeploymentName` | Deployed model name (default: `gpt-4o`) |
| `AzureOpenAI:MaxTokens` | `AzureOpenAI__MaxTokens` | Max tokens per response (default: `1024`) |
| `AzureOpenAI:Temperature` | `AzureOpenAI__Temperature` | Sampling temperature 0–2 (default: `0.7`) |
| `AzureSearch:Endpoint` | `AzureSearch__Endpoint` | Azure AI Search endpoint URL |
| `AzureSearch:ApiKey` | `AzureSearch__ApiKey` | Azure AI Search API key |
| `AzureSearch:IndexName` | `AzureSearch__IndexName` | Name of the search index |
| `AzureSearch:ContentField` | `AzureSearch__ContentField` | Index field holding text (default: `content`) |
| `AzureSearch:TitleField` | `AzureSearch__TitleField` | Index field holding the title (default: `title`) |
| `AzureSearch:TopK` | `AzureSearch__TopK` | Number of documents to retrieve (default: `3`) |
| `Rag:EnabledByDefault` | `Rag__EnabledByDefault` | Start with RAG on or off (default: `true`) |
| `Rag:SystemPrompt` | `Rag__SystemPrompt` | System message for every conversation |

## Running the application

```bash
cd src/AzureChat

# Set secrets via environment variables (recommended)
export AzureOpenAI__Endpoint="https://<your-resource>.openai.azure.com/"
export AzureOpenAI__ApiKey="<your-key>"
export AzureSearch__Endpoint="https://<your-resource>.search.windows.net"
export AzureSearch__ApiKey="<your-key>"
export AzureSearch__IndexName="<your-index>"

dotnet run
```

## Chat commands

| Command | Description |
|---|---|
| `/rag on` | Enable RAG document retrieval |
| `/rag off` | Disable RAG (model knowledge only) |
| `/rag` | Show current RAG status |
| `/clear` | Clear conversation history |
| `/config` | Show active configuration |
| `/help` | Show command list |
| `/exit` | Quit |

## Running the tests

```bash
dotnet test tests/AzureChat.Tests/
```
