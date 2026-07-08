using System.Diagnostics;
using System.Threading.Channels;
using AvellSucks.Api;
using AvellSucks.Core;
using AvellSucks.Core.Models;
using Microsoft.Extensions.Hosting;

namespace AvellSucks.Server;

public sealed class ConsoleEventPublisher : IEventPublisher, IHostedService
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<SystemInfo> _channel = Channel.CreateBounded<SystemInfo>(10);

    public Task<SystemInfo> NextAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct).AsTask();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(ProduceLoop);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        return Task.CompletedTask;
    }

    private async Task ProduceLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var info = BuildSnapshot();
                await _channel.Writer.WriteAsync(info, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Event publish error: {ex}");
            }

            await Task.Delay(1000, _cts.Token);
        }
    }

    private static SystemInfo BuildSnapshot()
        => SystemSnapshotBuilder.Build();
}
