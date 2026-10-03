using GameBoy.Debug.Core;
using GameBoy.Debug.Emulator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<ManagedGameBoyDebugSession>();
builder.Services.AddSingleton<IGameBoyDebugSession>(provider =>
    new SynchronizedGameBoyDebugSession(provider.GetRequiredService<ManagedGameBoyDebugSession>()));
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
