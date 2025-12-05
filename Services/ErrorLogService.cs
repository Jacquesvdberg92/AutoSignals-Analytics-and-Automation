using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.Extensions.DependencyInjection;

public class ErrorLogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramBotService _telegramBotService;

    public ErrorLogService(IServiceScopeFactory scopeFactory, TelegramBotService telegramBotService)
    {
        _scopeFactory = scopeFactory;
        _telegramBotService = telegramBotService;
    }

    public async Task LogErrorAsync(string message, string? stackTrace = null, string? source = null, string? additionalData = null)
    {
        try
        {
            // 1. Save to DB
            using (var scope = _scopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var errorLog = new ErrorLog
                {
                    Message = message,
                    StackTrace = stackTrace,
                    Source = source,
                    AdditionalData = additionalData,
                    Timestamp = DateTime.UtcNow
                };

                dbContext.ErrorLogs.Add(errorLog);
                await dbContext.SaveChangesAsync();
            }

            // 2. Send to Telegram
            // Telegram has a max message length, causing some messages not to send, by truncating it it ensure that you get a notification of the error
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

            await _telegramBotService.LoggError(telegramMessage);
        }
        catch (Exception ex)
        {
            // Fallback: notify in Telegram group that error logging failed
            var fallbackMessage = $"<b>CRITICAL: ErrorLogService failed</b>\n"
                + $"<b>Original error:</b> {message}\n"
                + $"<b>Logging failure:</b> {ex.Message}\n"
                + (string.IsNullOrWhiteSpace(ex.StackTrace) ? "" : $"\n<pre>{ex.StackTrace}</pre>");


            await _telegramBotService.LoggError(fallbackMessage);

        }
    }
}
