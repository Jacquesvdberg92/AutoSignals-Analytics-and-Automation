using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services.NOWPayments
{
    /// <summary>
    /// Background service that runs every 2 minutes and recovers payments that received
    /// an intermediate-status IPN but never received a <c>finished</c> IPN.
    /// Makes the payment flow self-healing for IPN delivery failures (misconfigured URL,
    /// server downtime, network errors, etc.).
    /// </summary>
    public class NOWPaymentsRecoveryService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NOWPaymentsRecoveryService> _logger;

        public NOWPaymentsRecoveryService(
            IServiceScopeFactory scopeFactory,
            ILogger<NOWPaymentsRecoveryService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                    await TryRecoverPendingPaymentsAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NOWPayments recovery: unhandled error in recovery cycle.");
                }
            }
        }

        private async Task TryRecoverPendingPaymentsAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context  = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            var provider = scope.ServiceProvider.GetRequiredService<NOWPaymentsSubscriptionProvider>();
            var webhook  = scope.ServiceProvider.GetRequiredService<NOWPaymentsWebhookService>();

            // Only look at events from the last 24 hours to avoid processing ancient orphaned records.
            var cutoff = DateTime.UtcNow.AddHours(-24);

            // Find all NOWPayments order_ids that have any event in the window.
            var withAnyEvent = await context.SubscriptionEvents
                .Where(e => e.Provider == "NOWPayments"
                         && e.OccurredAt >= cutoff
                         && e.ExternalSubscriptionId != null)
                .Select(e => e.ExternalSubscriptionId!)
                .Distinct()
                .ToListAsync(ct);

            if (withAnyEvent.Count == 0) return;

            // Exclude order_ids that already have an activation event.
            var activatedList = await context.SubscriptionEvents
                .Where(e => e.Provider == "NOWPayments"
                         && (e.EventType == SubscriptionEventTypes.SubscriptionCreated
                          || e.EventType == SubscriptionEventTypes.SubscriptionUpgraded)
                         && e.ExternalSubscriptionId != null)
                .Select(e => e.ExternalSubscriptionId!)
                .ToListAsync(ct);

            var alreadyActivated = activatedList.ToHashSet();

            var toRecover = withAnyEvent
                .Where(o => !alreadyActivated.Contains(o))
                .ToList();

            if (toRecover.Count == 0) return;

            _logger.LogInformation(
                "NOWPayments recovery: {Count} order(s) without activation event — checking NOWPayments API.",
                toRecover.Count);

            foreach (var orderId in toRecover)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var rawJson = await provider.GetFinishedPaymentRawByOrderIdAsync(orderId);
                    if (rawJson == null) continue;

                    _logger.LogInformation(
                        "NOWPayments recovery: found finished payment for order {OrderId} — processing.",
                        orderId);
                    await webhook.HandleEventAsync(rawJson);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "NOWPayments recovery: error processing order {OrderId}.", orderId);
                }
            }
        }
    }
}
