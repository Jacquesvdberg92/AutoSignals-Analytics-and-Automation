using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

namespace AutoSignals.Services
{
    public class VisitTrackingService : BackgroundService
    {
        private readonly Channel<UserVisit> _queue = Channel.CreateBounded<UserVisit>(
            new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VisitTrackingService> _logger;
        private DateTime _lastPurgeRun = DateTime.MinValue;

        public VisitTrackingService(IServiceScopeFactory scopeFactory, ILogger<VisitTrackingService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void Enqueue(UserVisit visit) => _queue.Writer.TryWrite(visit);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    await FlushAsync(stoppingToken);

                    if (DateTime.UtcNow - _lastPurgeRun > TimeSpan.FromHours(24))
                    {
                        await PurgeOldVisitsAsync(stoppingToken);
                        _lastPurgeRun = DateTime.UtcNow;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "VisitTrackingService loop error");
                }
            }
        }

        private async Task FlushAsync(CancellationToken ct)
        {
            var batch = new List<UserVisit>(200);
            while (batch.Count < 200 && _queue.Reader.TryRead(out var item))
                batch.Add(item);

            if (batch.Count == 0) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                db.UserVisits.AddRange(batch);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush {Count} visit records", batch.Count);
            }
        }

        private async Task PurgeOldVisitsAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var retentionDays = 90;
                var setting = await db.AdminSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == "VisitRetentionDays", ct);
                if (setting != null && int.TryParse(setting.Value, out var d))
                    retentionDays = d;

                var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
                int purged;
                do
                {
                    purged = await db.Database.ExecuteSqlRawAsync(
                        "DELETE TOP (2000) FROM UserVisits WHERE Timestamp < {0}", new object[] { cutoff }, ct);
                } while (purged > 0 && !ct.IsCancellationRequested);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge old visit records");
            }
        }
    }
}
