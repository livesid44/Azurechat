using AzureChat.Configuration;
using AzureChat.Models;
using AzureChat.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace AzureChat;

/// <summary>
/// Interactive console UI for chatting with Azure OpenAI + optional RAG,
/// and for triggering the Blob→Cosmos ingestion pipeline.
/// </summary>
public sealed class ChatUi
{
    private readonly IRagService _rag;
    private readonly IIngestionPipelineService _ingestion;
    private readonly AzureOpenAIOptions _openAIOptions;
    private readonly AzureSearchOptions _searchOptions;
    private readonly BlobStorageOptions _blobOptions;
    private readonly CosmosDbOptions _cosmosOptions;
    private readonly ILogger<ChatUi> _logger;

    private readonly List<ChatMessage> _history = new();

    public ChatUi(
        IRagService rag,
        IIngestionPipelineService ingestion,
        IOptions<AzureOpenAIOptions> openAIOptions,
        IOptions<AzureSearchOptions> searchOptions,
        IOptions<BlobStorageOptions> blobOptions,
        IOptions<CosmosDbOptions> cosmosOptions,
        ILogger<ChatUi> logger)
    {
        _rag = rag;
        _ingestion = ingestion;
        _openAIOptions = openAIOptions.Value;
        _searchOptions = searchOptions.Value;
        _blobOptions = blobOptions.Value;
        _cosmosOptions = cosmosOptions.Value;
        _logger = logger;
    }

    /// <summary>Runs the interactive chat loop until the user exits.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        RenderHeader();
        RenderConfig();
        RenderHelp();

        while (!cancellationToken.IsCancellationRequested)
        {
            string input = AnsiConsole.Ask<string>("[bold green]You[/]> ").Trim();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (await TryHandleCommandAsync(input, cancellationToken))
                continue;

            await SendMessageAsync(input, cancellationToken);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task SendMessageAsync(string question, CancellationToken cancellationToken)
    {
        try
        {
            RagResponse response = await AnsiConsole
                .Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[dim]Thinking…[/]", _ => _rag.AskAsync(question, _history, cancellationToken));

            // Append turns to history (without system prompt — that's added per-request by RagService).
            _history.Add(new ChatMessage("user", question));
            _history.Add(new ChatMessage("assistant", response.Answer));

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold blue]Assistant[/]:");
            AnsiConsole.WriteLine(response.Answer);

            if (response.Sources.Count > 0)
            {
                AnsiConsole.WriteLine();
                var table = new Table().Border(TableBorder.Simple).AddColumn("Source").AddColumn("Score");
                foreach (var src in response.Sources)
                    table.AddRow(Markup.Escape(src.Title.Length > 0 ? src.Title : "(untitled)"), $"{src.Score:F4}");
                AnsiConsole.MarkupLine("[dim]Sources used:[/]");
                AnsiConsole.Write(table);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during chat request");
            AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>Returns true when <paramref name="input"/> was a UI command.</summary>
    private async Task<bool> TryHandleCommandAsync(string input, CancellationToken cancellationToken)
    {
        switch (input.ToLowerInvariant())
        {
            case "/help":
                RenderHelp();
                return true;

            case "/rag on":
                _rag.IsEnabled = true;
                AnsiConsole.MarkupLine("[yellow]RAG enabled.[/]");
                return true;

            case "/rag off":
                _rag.IsEnabled = false;
                AnsiConsole.MarkupLine("[yellow]RAG disabled — answering from model knowledge only.[/]");
                return true;

            case "/rag":
                AnsiConsole.MarkupLine($"[yellow]RAG is currently [bold]{(_rag.IsEnabled ? "ON" : "OFF")}[/].[/]");
                return true;

            case "/ingest":
                await RunIngestionAsync(cancellationToken);
                return true;

            case "/clear":
                _history.Clear();
                AnsiConsole.Clear();
                RenderHeader();
                RenderConfig();
                AnsiConsole.MarkupLine("[yellow]Conversation cleared.[/]");
                return true;

            case "/config":
                RenderConfig();
                return true;

            case "/exit":
            case "/quit":
            case "exit":
            case "quit":
                AnsiConsole.MarkupLine("[dim]Goodbye![/]");
                Environment.Exit(0);
                return true;

            default:
                return false;
        }
    }

    private async Task RunIngestionAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold]Starting ingestion[/] from blob container [yellow]{Markup.Escape(_blobOptions.ContainerName.Length > 0 ? _blobOptions.ContainerName : "(not set)")}[/] → Cosmos DB [yellow]{Markup.Escape(_cosmosOptions.ContainerName.Length > 0 ? _cosmosOptions.ContainerName : "(not set)")}[/]");
        AnsiConsole.WriteLine();

        IngestionSummary summary;

        try
        {
            summary = await AnsiConsole
                .Progress()
                .Columns(new TaskDescriptionColumn(), new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[bold]Ingesting blobs…[/]");
                    var result = await _ingestion.RunAsync(cancellationToken);
                    task.StopTask();
                    return result;
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion pipeline error");
            AnsiConsole.MarkupLine($"[bold red]Ingestion error:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.WriteLine();
            return;
        }

        AnsiConsole.MarkupLine($"[bold green]✔ Succeeded:[/] {summary.Succeeded}   [bold red]✘ Failed:[/] {summary.Failed}");

        if (summary.Errors.Count > 0)
        {
            foreach (string error in summary.Errors)
                AnsiConsole.MarkupLine($"  [red]•[/] {Markup.Escape(error)}");
        }

        AnsiConsole.WriteLine();
    }

    private void RenderHeader()
    {
        AnsiConsole.Write(new FigletText("Azure AI").Color(Color.SteelBlue1));
        AnsiConsole.MarkupLine("[bold steelblue1]Assistant[/]");
        AnsiConsole.MarkupLine("[dim]Azure OpenAI + Azure AI Search (RAG) + Blob→Cosmos Ingestion[/]");
        AnsiConsole.WriteLine();
    }

    private void RenderConfig()
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap());
        grid.AddColumn();

        grid.AddRow("[bold]OpenAI Endpoint[/]", Markup.Escape(_openAIOptions.Endpoint.Length > 0 ? _openAIOptions.Endpoint : "(not set)"));
        grid.AddRow("[bold]Deployment[/]", Markup.Escape(_openAIOptions.DeploymentName));
        grid.AddRow("[bold]Search Endpoint[/]", Markup.Escape(_searchOptions.Endpoint.Length > 0 ? _searchOptions.Endpoint : "(not set)"));
        grid.AddRow("[bold]Search Index[/]", Markup.Escape(_searchOptions.IndexName.Length > 0 ? _searchOptions.IndexName : "(not set)"));
        grid.AddRow("[bold]RAG[/]", _rag.IsEnabled ? "[green]ON[/]" : "[red]OFF[/]");
        grid.AddRow("[bold]Blob Container[/]", Markup.Escape(_blobOptions.ContainerName.Length > 0 ? _blobOptions.ContainerName : "(not set)"));
        grid.AddRow("[bold]Cosmos Container[/]", Markup.Escape(_cosmosOptions.ContainerName.Length > 0 ? _cosmosOptions.ContainerName : "(not set)"));

        AnsiConsole.Write(new Panel(grid)
            .Header("[bold]Configuration[/]")
            .BorderColor(Color.Grey)
            .Padding(1, 0));
        AnsiConsole.WriteLine();
    }

    private static void RenderHelp()
    {
        var table = new Table().Border(TableBorder.Simple)
            .AddColumn("[bold]Command[/]")
            .AddColumn("[bold]Description[/]");

        table.AddRow("/rag on|off", "Enable or disable Retrieval-Augmented Generation");
        table.AddRow("/rag", "Show current RAG status");
        table.AddRow("/ingest", "Run Blob → Cosmos DB ingestion pipeline");
        table.AddRow("/clear", "Clear conversation history");
        table.AddRow("/config", "Show current configuration");
        table.AddRow("/help", "Show this help");
        table.AddRow("/exit", "Quit the application");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
