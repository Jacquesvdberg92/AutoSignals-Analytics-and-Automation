using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Channels;

public class ErrorLogService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramNotifier _telegramNotifier;
    private readonly Channel<ErrorLog> _queue = Channel.CreateUnbounded<ErrorLog>(
        new UnboundedChannelOptions { SingleReader = true });

    public ErrorLogService(IServiceScopeFactory scopeFactory, ITelegramNotifier telegramNotifier)
    {
        _scopeFactory = scopeFactory;
        _telegramNotifier = telegramNotifier;
    }

    public async Task LogErrorAsync(string message, string? stackTrace = null, string? source = null, string? additionalData = null)
    {
        try
        {
            // 1. Enqueue for batch DB write (no per-call DB round-trip)
            _queue.Writer.TryWrite(new ErrorLog
            {
                Message = message,
                StackTrace = stackTrace,
                Source = source,
                AdditionalData = additionalData,
                Timestamp = DateTime.UtcNow
            });

            // 2. Send to Telegram immediately for real-time alerts
            const int maxLength = 1000;

            string Truncate(string? value, int max)
            {
                if (string.IsNullOrEmpty(value)) return string.Empty;
                return value.Length > max ? value.Substring(0, max) + "...(truncated)" : value;
            }

            var telegramMessage = $"<b>Error:</b> {message}"
                + (string.IsNullOrWhiteSpace(source) ? "" : $"\n<b>Source:</b> {source}")
                + (string.IsNullOrWhiteSpace(stackTrace) ? "" : $"\n<pre>{Truncate(stackTrace, maxLength)}</pre>")
                + (string.IsNullOrWhiteSpace(additionalData) ? "" : $"\n<b>Data:</b> {Truncate(additionalData, maxLength)}");

            await _telegramNotifier.LoggError(telegramMessage);
        }
        catch (Exception ex)
        {
            // Fallback: notify in Telegram group that error logging failed
            var fallbackMessage = $"<b>CRITICAL: ErrorLogService failed</b>\n"
                + $"<b>Original error:</b> {message}\n"
                + $"<b>Logging failure:</b> {ex.Message}\n"
                + (string.IsNullOrWhiteSpace(ex.StackTrace) ? "" : $"\n<pre>{ex.StackTrace}</pre>");

            await _telegramNotifier.LoggError(fallbackMessage);
        }
    }

    // Background flusher: batches all queued logs into a single DB write every 5 seconds
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await FlushAsync();
        }

        // Final flush on shutdown
        await FlushAsync();
    }

    private async Task FlushAsync()
    {
        var batch = new List<ErrorLog>();
        while (_queue.Reader.TryRead(out var log))
            batch.Add(log);

        if (batch.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            db.ErrorLogs.AddRange(batch);
            await db.SaveChangesAsync();
        }
        catch
        {
            // Re-enqueue so logs are not lost on transient DB failures
            foreach (var log in batch)
                _queue.Writer.TryWrite(log);
        }
    }
}
