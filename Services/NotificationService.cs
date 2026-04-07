using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services
{
    public class NotificationService : INotificationService
    {
        private readonly AutoSignalsDbContext _context;
        private readonly ITelegramNotifier _telegramNotifier;
        private readonly IEmailSender _emailSender;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            AutoSignalsDbContext context,
            ITelegramNotifier telegramNotifier,
            IEmailSender emailSender,
            UserManager<IdentityUser> userManager,
            ILogger<NotificationService> logger)
        {
            _context = context;
            _telegramNotifier = telegramNotifier;
            _emailSender = emailSender;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task NotifyOrderExecutedAsync(string userId, Order order, CancellationToken cancellationToken = default)
        {
            try
            {
                var eventType = ResolveEventType(order.Description);
                var settings = await GetOrDefaultAsync(userId);

                if (ShouldSendTelegram(settings, eventType))
                {
                    var text = FormatTelegramMessage(order, eventType);
                    await _telegramNotifier.SendDirectMessageToUserAsync(userId, text, cancellationToken);
                }

                if (ShouldSendEmail(settings, eventType))
                {
                    var identityUser = await _userManager.FindByIdAsync(userId);
                    if (!string.IsNullOrWhiteSpace(identityUser?.Email))
                    {
                        var (subject, html) = FormatEmailMessage(order, eventType);
                        await _emailSender.SendEmailAsync(identityUser.Email, subject, html);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotificationService failed for user {UserId}, order {OrderId}.", userId, order?.Id);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static NotificationEventType ResolveEventType(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return NotificationEventType.OrderExecuted;

            if (description.Contains("Take Profit", StringComparison.OrdinalIgnoreCase))
                return NotificationEventType.TakeProfitHit;

            if (description.Contains("Stoploss", StringComparison.OrdinalIgnoreCase) ||
                description.Equals("Moonbag Order", StringComparison.OrdinalIgnoreCase))
                return NotificationEventType.StopLossHit;

            return NotificationEventType.OrderExecuted;
        }

        private static bool ShouldSendTelegram(UserNotificationSettings s, NotificationEventType t) => t switch
        {
            NotificationEventType.TakeProfitHit => s.TelegramTakeProfitHit,
            NotificationEventType.StopLossHit   => s.TelegramStopLossHit,
            _                                   => s.TelegramOrderExecuted
        };

        private static bool ShouldSendEmail(UserNotificationSettings s, NotificationEventType t) => t switch
        {
            NotificationEventType.TakeProfitHit => s.EmailTakeProfitHit,
            NotificationEventType.StopLossHit   => s.EmailStopLossHit,
            _                                   => s.EmailOrderExecuted
        };

        private async Task<UserNotificationSettings> GetOrDefaultAsync(string userId) =>
            await _context.UserNotificationSettings.FirstOrDefaultAsync(s => s.UserId == userId)
            ?? new UserNotificationSettings { UserId = userId };

        // ── Message formatters ────────────────────────────────────────────────────

        private static string FormatTelegramMessage(Order order, NotificationEventType type)
        {
            var (icon, header) = type switch
            {
                NotificationEventType.TakeProfitHit => ("🎉", "Take Profit Hit"),
                NotificationEventType.StopLossHit   => ("⚠️", "Stop-Loss Hit"),
                _                                   => ("✅", "Order Executed")
            };

            return $"{icon} <b>{header}</b>\n" +
                   $"<b>Symbol:</b> {order.Symbol}\n" +
                   $"<b>Side:</b> {order.Side}\n" +
                   $"<b>Description:</b> {order.Description}\n" +
                   $"<b>Price:</b> {order.Price?.ToString() ?? "N/A"}\n" +
                   $"<b>Size:</b> {order.Size}\n" +
                   $"<b>Leverage:</b> {order.Leverage}x\n" +
                   $"<b>Status:</b> {order.Status}\n" +
                   $"<b>Time (UTC):</b> {order.Time:yyyy-MM-dd HH:mm:ss}\n\n" +
                   $"<a href=\"https://autosignals.xyz\">AutoSignals.xyz</a>";
        }

        private static (string subject, string html) FormatEmailMessage(Order order, NotificationEventType type)
        {
            var (icon, header, accent) = type switch
            {
                NotificationEventType.TakeProfitHit => ("🎉", "Take Profit Hit",  "#26A69A"),
                NotificationEventType.StopLossHit   => ("⚠️", "Stop-Loss Hit",    "#EF5350"),
                _                                   => ("✅", "Order Executed",   "#F5A623")
            };

            var subject = $"{icon} AutoSignals — {header}: {order.Symbol}";

            var html = $"""
<!DOCTYPE html>
<html><head><meta charset="utf-8"/></head>
<body style="margin:0;padding:0;background:#0d0d0d;font-family:Arial,sans-serif;">
  <table width="100%" cellpadding="0" cellspacing="0" bgcolor="#0d0d0d">
    <tr><td align="center" style="padding:20px 0;">
      <table width="600" cellpadding="0" cellspacing="0" style="background:#1a1a2e;border-radius:12px;overflow:hidden;">
        <tr><td><img src="cid:logo-header" width="600" alt="AutoSignals" style="display:block;width:100%;"/></td></tr>
        <tr><td style="padding:30px 40px;">
          <h1 style="color:{accent};margin:0 0 20px;">{icon} {header}</h1>
          <table width="100%" cellpadding="8" cellspacing="0" style="border-collapse:collapse;">
            <tr style="border-bottom:1px solid #333;"><td style="color:#999;width:40%;">Symbol</td><td style="color:#fff;font-weight:bold;">{order.Symbol}</td></tr>
            <tr style="border-bottom:1px solid #333;"><td style="color:#999;">Side</td><td style="color:#fff;">{order.Side}</td></tr>
            <tr style="border-bottom:1px solid #333;"><td style="color:#999;">Description</td><td style="color:#fff;">{order.Description}</td></tr>
            <tr style="border-bottom:1px solid #333;"><td style="color:#999;">Price</td><td style="color:#fff;">{order.Price?.ToString() ?? "N/A"}</td></tr>
            <tr style="border-bottom:1px solid #333;"><td style="color:#999;">Size</td><td style="color:#fff;">{order.Size}</td></tr>
            <tr style="border-bottom:1px solid #333;"><td style="color:#999;">Leverage</td><td style="color:#fff;">{order.Leverage}x</td></tr>
            <tr><td style="color:#999;">Time (UTC)</td><td style="color:#fff;">{order.Time:yyyy-MM-dd HH:mm:ss}</td></tr>
          </table>
        </td></tr>
        <tr><td style="padding:20px 40px;background:#111;text-align:center;">
          <p style="color:#666;font-size:12px;margin:0;">
            ⚠️ Trading is NOT risk free &nbsp;|&nbsp; ⚠️ Don't trade what you can't lose<br/>
            <a href="https://autosignals.xyz" style="color:{accent};">AutoSignals.xyz</a>
            &nbsp;|&nbsp;
            <a href="https://autosignals.xyz/settings" style="color:#666;">Manage Notifications</a>
          </p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body></html>
""";

            return (subject, html);
        }
    }

    public enum NotificationEventType
    {
        OrderExecuted,
        TakeProfitHit,
        StopLossHit
    }
}
