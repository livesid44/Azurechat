using AzureChat;
using AzureChat.Configuration;
using AzureChat.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Build configuration from appsettings.json + environment variables.
// Environment variable overrides follow the convention:
//   AzureOpenAI__Endpoint, AzureOpenAI__ApiKey, AzureSearch__Endpoint, etc.
IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// Set up DI container.
ServiceCollection services = new();

services.AddLogging(b =>
{
    b.AddConsole();
    b.AddConfiguration(configuration.GetSection("Logging"));
});

services
    .Configure<AzureOpenAIOptions>(configuration.GetSection(AzureOpenAIOptions.SectionName))
    .Configure<AzureSearchOptions>(configuration.GetSection(AzureSearchOptions.SectionName))
    .Configure<RagOptions>(configuration.GetSection(RagOptions.SectionName));

services
    .AddSingleton<ISearchService, SearchService>()
    .AddSingleton<IChatService, ChatService>()
    .AddSingleton<IRagService, RagService>()
    .AddSingleton<ChatUi>();

await using ServiceProvider serviceProvider = services.BuildServiceProvider();

var ui = serviceProvider.GetRequiredService<ChatUi>();

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await ui.RunAsync(cts.Token);
