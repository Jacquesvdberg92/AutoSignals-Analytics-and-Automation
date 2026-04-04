using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace AutoSignals.Services
{
    public interface IAnalyticsService
    {
        /// <summary>
        /// Increments the in-memory page-view counter for <paramref name="pageName"/>.
        /// No DB access occurs here — counts are flushed to the DB every 60 seconds.
        /// </summary>
        void Increment(string pageName);
    }

    /// <summary>
    /// Singleton background service that batches page-view counts in memory and flushes
    /// them to the Analytics table once per minute, eliminating per-request DB writes.
    /// </summary>
    public class AnalyticsService : IAnalyticsService, IHostedService, IDisposable
    {
        private readonly ConcurrentDictionary<(string PageName, DateTime Date), int> _counts = new();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AnalyticsService> _logger;
        private Timer? _flushTimer;

        public AnalyticsService(IServiceScopeFactory scopeFactory, ILogger<AnalyticsService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void Increment(string pageName)
        {
            var key = (pageName, DateTime.UtcNow.Date);
            _counts.AddOrUpdate(key, 1, (_, existing) => existing + 1);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _flushTimer = new Timer(FlushCallback, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _flushTimer?.Change(Timeout.Infinite, 0);
            await FlushAsync();
        }

        public void Dispose() => _flushTimer?.Dispose();

        private void FlushCallback(object? state) => _ = FlushAsync();

        private async Task FlushAsync()
        {
            if (_counts.IsEmpty) return;

            // Atomically drain all pending counts
            var toFlush = new Dictionary<(string, DateTime), int>();
            foreach (var key in _counts.Keys.ToList())
            {
                if (_counts.TryRemove(key, out var count))
                    toFlush[key] = count;
            }

            if (toFlush.Count == 0) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var pageNames = toFlush.Keys.Select(k => k.Item1).Distinct().ToList();
                var dates = toFlush.Keys.Select(k => k.Item2).Distinct().ToList();

                var existing = await db.Analytics
                    .Where(a => pageNames.Contains(a.PageName) && dates.Contains(a.Date))
                    .ToListAsync();

                var existingLookup = existing.ToDictionary(a => (a.PageName, a.Date));

                foreach (var (key, count) in toFlush)
                {
                    if (existingLookup.TryGetValue(key, out var record))
                    {
                        record.Views += count;
                    }
                    else
                    {
                        db.Analytics.Add(new Analytics
                        {
                            PageName = key.Item1,
                            Date = key.Item2,
                            Views = count
                        });
                    }
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush analytics counts to the database.");
                // Put the counts back so they are not lost
                foreach (var (key, count) in toFlush)
                    _counts.AddOrUpdate(key, count, (_, existing) => existing + count);
            }
        }
    }
}
