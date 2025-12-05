using AutoSignals.Models;
using AutoSignals.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Telegram.Bot.Types.ReplyMarkups;

namespace AutoSignals.Services
{
    public class SignalPerformanceService
    {
        private readonly AutoSignalsDbContext _context;
        private readonly TelegramBotService _telegramBotService;
        private readonly IWebHostEnvironment _env;
        private readonly IServiceScopeFactory _scopeFactory;

        public SignalPerformanceService(AutoSignalsDbContext context, TelegramBotService telegramBotService, IServiceScopeFactory scopeFactory, IWebHostEnvironment env)
        {
            _context = context;
            _telegramBotService = telegramBotService ?? throw new ArgumentNullException(nameof(telegramBotService));
            _scopeFactory = scopeFactory;
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        
        //private float CalculateLossPercentage(Signal signal, decimal currentPrice)
        //{
        //    var entryPrice = signal.Entry;
        //    var leverage = signal.Leverage;
        //    bool isLongTrade = signal.Side.Equals("long", StringComparison.OrdinalIgnoreCase);

        //    if (isLongTrade)
        //    {
        //        return (float)Math.Round((((currentPrice - (decimal)entryPrice) / (decimal)entryPrice) * 100 * leverage), 2);
        //    }
        //    else // Short trade
        //    {
        //        return (float)Math.Round(((((decimal)entryPrice - currentPrice) / (decimal)entryPrice) * 100 * leverage), 2);
        //    }
        //}

        private float CalculateProfitPercentage(Signal signal, decimal takeProfitPrice)
        {
            var entryPrice = signal.Entry;
            var leverage = signal.Leverage;
            bool isLongTrade = signal.Side.Equals("long", StringComparison.OrdinalIgnoreCase);

            if (isLongTrade)
            {
                return (float)Math.Round((((takeProfitPrice - (decimal)entryPrice) / (decimal)entryPrice) * 100 * leverage), 2);
            }
            else // Short trade
            {
                return (float)Math.Round(((((decimal)entryPrice - takeProfitPrice) / (decimal)entryPrice) * 100 * leverage), 2);
            }
        }

        public Image RenderTextToImage(
    string text,
    string logoPath,
    string qrCode1Path,
    int width = 1000,
    int padding = 40)
        {
            // Use a larger, bold font for the main text
            var font = new Font("Segoe UI", 48, FontStyle.Bold, GraphicsUnit.Pixel);
            var watermarkFont = new Font("Segoe UI", 28, FontStyle.Italic, GraphicsUnit.Pixel);
            var watermarkBrush = new SolidBrush(Color.FromArgb(30, 255, 255, 255)); // Semi-transparent white

            var textAreaWidth = width - 2 * padding;
            var tempGraphics = Graphics.FromImage(new Bitmap(width, 1));
            var textSize = tempGraphics.MeasureString(text, font, textAreaWidth);
            tempGraphics.Dispose();

            var logo = Image.FromFile(logoPath);
            var qr1 = Image.FromFile(qrCode1Path);

            // Calculate logo size to fill the width and preserve aspect ratio
            float logoAspect = (float)logo.Width / logo.Height;
            int logoDrawWidth = width;
            int logoDrawHeight = (int)(width / logoAspect);
            int logoX = 0;
            int logoY = 0; // Top edge

            int qrSize = 140;
            int spacing = 30; // Slightly more spacing for clarity

            // Calculate total image height: logo + spacing + text + spacing + QR + padding
            int totalHeight = logoDrawHeight + spacing + (int)textSize.Height + spacing + qrSize + padding;

            var finalImage = new Bitmap(width, totalHeight);
            using var g = Graphics.FromImage(finalImage);
            g.Clear(Color.FromArgb(18, 18, 18));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // 🔁 Watermark background
            var wmText = " AutoSignals.xyz • @CL_AutoSignals_Bot ";
            var wmSpacingX = 250;
            var wmSpacingY = 100;

            for (int y = -totalHeight; y < totalHeight * 2; y += wmSpacingY)
            {
                for (int x = -width; x < width * 2; x += wmSpacingX)
                {
                    g.TranslateTransform(x, y);
                    g.RotateTransform(-30);
                    g.DrawString(wmText, watermarkFont, watermarkBrush, 0, 0);
                    g.ResetTransform();
                }
            }

            // Draw logo at the very top
            var logoRect = new Rectangle(logoX, logoY, logoDrawWidth, logoDrawHeight);
            g.DrawImage(logo, logoRect);

            // Draw text below the logo, with spacing
            var textRect = new RectangleF(padding, logoDrawHeight + spacing, textAreaWidth, textSize.Height);

            // Optional: Draw a subtle shadow for better readability
            var shadowOffset = 2;
            using (var shadowBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                g.DrawString(text, font, shadowBrush, textRect.X + shadowOffset, textRect.Y + shadowOffset);
            }
            g.DrawString(text, font, Brushes.White, textRect);

            // Draw QR code at the bottom left
            var qrY = totalHeight - qrSize - padding;
            g.DrawImage(qr1, new Rectangle(padding, qrY, qrSize, qrSize));

            // Placeholder for Second QR
            var placeholderRect = new Rectangle(width - padding - qrSize, qrY, qrSize, qrSize);
            g.FillRectangle(new SolidBrush(Color.FromArgb(40, 255, 255, 255)), placeholderRect);
            g.DrawString("", font, Brushes.White, placeholderRect); //2nd QR placeholder

            return finalImage;
        }

        private string GetEncouragingMessage()
        {
            var messages = new List<string>
            {
                "Don't worry, every loss is a step towards success!",
                "Keep your head up! The market is full of opportunities.",
                "Stay positive, better trades are ahead!",
                "Every trader faces losses, it's part of the journey.",
                "Learn from this and come back stronger!",
                "Success is built on the lessons learned from failure.",
                "This is just one trade; the next one could be your big win!",
                "Even the best traders face losses—keep going!",
                "Losses are temporary; your determination is permanent.",
                "Use this as a chance to refine your strategy.",
                "Every setback is a setup for a comeback.",
                "Trading is a marathon, not a sprint—keep pacing yourself!",
                "Small losses pave the way for big gains in the future.",
                "The market will always provide another opportunity.",
                "Shake it off! Tomorrow is a new trading day.",
                "You’re improving with every trade, win or lose.",
                "Mistakes are proof you’re trying—keep learning.",
                "Keep moving forward; success is within reach.",
                "Patience and persistence will reward you in the long run.",
                "Losses are a reminder to always stay disciplined."
            };
            var random = new Random();
            return messages[random.Next(messages.Count)];
        }

        private string GetPraiseMessage()
        {
            var messages = new List<string>
            {
                "Great job! You've hit a take profit!",
                "Congratulations on your successful trade!",
                "Well done! Keep up the good work!",
                "Fantastic! Your strategy is paying off!",
                "Awesome! Another take profit achieved!",
                "Keep it up! Your hard work is paying off!",
                "Impressive! Another win for your trading journal!",
                "Your dedication is bringing great results!",
                "Amazing work! You're mastering the market!",
                "You've got the momentum—keep pushing forward!",
                "Outstanding trade! You're on fire!",
                "Brilliant! Your analysis was spot on!",
                "Another win in the books—great trading!",
                "Keep stacking those wins! You're doing amazing.",
                "Your consistency is leading to success—awesome job!",
                "You've turned insight into profit—well done!",
                "Smart trading pays off—keep the streak alive!",
                "Your skill is shining through—congratulations!",
                "Another profit, another step towards your goals!",
                "Success is a habit, and you're mastering it!"
            };
            var random = new Random();
            return messages[random.Next(messages.Count)];
        }

        public async Task TrackPerformance()
        {
            var now = DateTime.UtcNow;
            var signalPerformances = await _context.SignalPerformances
                .Where(s => s.Status == "Open" || s.Status == "Pending")
                .ToListAsync();
            var signals = await _context.Signals.ToListAsync();
            var priceData = await _context.GeneralAssetPrices.ToListAsync();

            foreach (var performance in signalPerformances)
            {
                var signal = signals.FirstOrDefault(s => s.Id == performance.SignalId);
                if (signal == null) continue;

                // Fetch relevant prices for this signal
                var relevantPrices = priceData
                    .Where(p => p.Symbol == signal.Symbol && p.Time >= performance.StartTime)// 
                    .OrderBy(p => p.Time)
                    .ToList();

                // Handle "Pending" signals
                if (performance.Status == "Pending")
                {
                    await HandlePendingSignal(performance, signal, relevantPrices);
                    continue;
                }

                // Handle "Open" signals
                if (performance.Status == "Open")
                {
                    await HandleOpenSignal(performance, signal, relevantPrices);
                }
            }

            try
            {
                await _context.SaveChangesAsync(); // Save changes to the database
            }
            catch (Exception ex)
            {
                using (var errorLogScope = _scopeFactory.CreateScope())
                {
                    var errorLogService = errorLogScope.ServiceProvider.GetRequiredService<ErrorLogService>();
                    await errorLogService.LogErrorAsync(
                        $"Failed to Track signal performance",
                        ex.StackTrace, "SignalPerformanceService", $"Inner Ex: {ex.InnerException}");
                }
            }

            var end = DateTime.UtcNow;
            var duration = end - now;
            System.Console.WriteLine($"Signal performance tracking completed in {duration.TotalSeconds} seconds.");
        }

        private async Task HandlePendingSignal(SignalPerformance performance, Signal signal, List<GeneralAssetPrice> relevantPrices)
        {
            // Fetch the provider for this signal
            var provider = await _context.Provider.FirstOrDefaultAsync(p => p.Name == signal.Provider);

            var entryPrice = (decimal)signal.Entry;
            var stoplossPrice = (decimal)signal.Stoploss;
            //var withinRange = relevantPrices.Any(p => Math.Abs(p.Price - (decimal)entryPrice) / (decimal)entryPrice <= 0.01m); -- This is the old way of checking within 1% range and started causing issues when all exchanges started averaging prices
            var withinRange = relevantPrices.Any(p =>
                signal.Side.Equals("long", StringComparison.OrdinalIgnoreCase)
                    ? p.Price >= Math.Min(stoplossPrice, entryPrice) && p.Price <= Math.Max(stoplossPrice, entryPrice)
                    : p.Price <= Math.Max(stoplossPrice, entryPrice) && p.Price >= Math.Min(stoplossPrice, entryPrice)
            );

            // Prepare buttons
            var buttons = new List<IEnumerable<InlineKeyboardButton>>
            {
                new[]
                {
                    InlineKeyboardButton.WithUrl("📚 Education", "https://autosignals.xyz/education/basics"),
                    InlineKeyboardButton.WithUrl("🛡️ Risk Management", "https://autosignals.xyz/education/risk-management")
                },
                new[]
                {
                    InlineKeyboardButton.WithUrl("🌐 Website", "https://AutoSignals.xyz"),
                    InlineKeyboardButton.WithUrl("💸 Exchanges", "https://autosignals.xyz/Exchanges")
                },
                new[]
                {
                    InlineKeyboardButton.WithUrl("📊 Analytics", "https://autosignals.xyz/Providers"),
                },
                new[]
                {
                    InlineKeyboardButton.WithUrl("🔑 Free Sign-up", "https://autosignals.xyz/Identity/Account/Login"),
                }

            };
            

            if (!string.IsNullOrWhiteSpace(provider?.Telegram))
            {
                buttons.Add(new[]
                {
                InlineKeyboardButton.WithUrl("📢 Source Group", provider.Telegram)
                });
            }

            if (withinRange)//
            {
                performance.Status = "Open";
                performance.StartTime = DateTime.Now;

                // Render message as plain text
                var messageText = $"""
Provider: {signal.Provider}

Signal ID: {signal.Id}
Symbol: {signal.Symbol}
Side: {signal.Side}
Leverage: {signal.Leverage}x
Entry: {signal.Entry}
Stop-Loss: {signal.Stoploss}
Take Profits:
{string.Join("\n", signal.TakeProfits.Split(',').Select((tp, index) => $"{index + 1}. {tp.Trim()}"))}

⚠Trading is NOT risk free
⚠Don't trade what you can't lose
⚠Manage your risk

Admin: @CryptoLifestyle_Admin
Website: https://AutoSignals.xyz
""";

                // Generate image
                var logoPath = Path.Combine(_env.WebRootPath, "assets", "images", "brand-logos", "signal-header.png");
                var qrCode1Path = Path.Combine(_env.WebRootPath, "assets", "images", "brand-logos", "cl_qr.png");
                var image = RenderTextToImage(messageText,logoPath,qrCode1Path);
                using var stream = new MemoryStream();
                image.Save(stream, ImageFormat.Png);

                // Send image
                var msgId = await _telegramBotService.PostMessageToGroupAsync(
                    message: "🚀 New Trade Signal",
                    cancellationToken: CancellationToken.None,
                    replyToMessageId: null,
                    messageThreadId: null, // <-- Topic ID here
                    imageStream: stream,
                    imageFileName: "new_trade.png",
                    buttons: buttons
                );

                performance.TelegramMessageId = msgId?.ToString(); // Save the Telegram message ID
                try
                {

                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    using (var errorLogScope = _scopeFactory.CreateScope())
                    {
                        var errorLogService = errorLogScope.ServiceProvider.GetRequiredService<ErrorLogService>();
                        await errorLogService.LogErrorAsync(
                            $"Failed to Track signal performance",
                            ex.StackTrace, "SignalPerformanceService.HandlePending", $"Inner Ex: {ex.InnerException}");
                    }
                }

            }
            else if ((DateTime.Now - performance.StartTime).TotalHours > 24)
            {
                performance.Status = "Canceled";
                performance.Notes = "Entry price not reached within 24 hours";
                performance.EndTime = DateTime.Now;

                // Prepare cancellation message
                var msg = $"""
<b>Signal Canceled ❌</b>

<b>Provider:</b> {signal.Provider}

<b>Signal ID:</b> {signal.Id}
<b>Symbol:</b> {signal.Symbol}  
<b>Side:</b> {signal.Side}

<i>Reason:</i> Did not reach entry in 24 hours.
""";

                int? replyToMessageId = null;
                if (int.TryParse(performance.TelegramMessageId, out var msgId))
                    replyToMessageId = msgId;

                // Send Cancellation Message as a reply -- Commented out as they will get a message of a canceled signal that they didn know existed
                //await _telegramBotService.PostMessageToGroupAsync(
                //    msg,
                //    CancellationToken.None,
                //    replyToMessageId: replyToMessageId
                //);
                try {                     
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    await _telegramBotService.LoggError($"Error saving SignalPerformance changes: {ex.Message}\n{ex.StackTrace}");
                    // Optionally, rethrow or handle as needed
                }
            }
        }


        private async Task HandleOpenSignal(SignalPerformance performance, Signal signal, List<GeneralAssetPrice> relevantPrices)
        {
            var takeProfits = await ParseTakeProfits(signal.TakeProfits);
            var achievedTakeProfits = string.IsNullOrEmpty(performance.AchievedTakeProfits)
                ? new HashSet<decimal>()
                : new HashSet<decimal>(performance.AchievedTakeProfits.Split(',')
                    .Select(tp => decimal.Parse(tp.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture)));
            bool isLongTrade = signal.Side.Equals("long", StringComparison.OrdinalIgnoreCase);
            var notifiedTakeProfits = string.IsNullOrEmpty(performance.NotifiedTakeProfits)
                ? new HashSet<decimal>()
                : new HashSet<decimal>(performance.NotifiedTakeProfits.Split(',')
                    .Select(tp => decimal.Parse(tp.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture)));

            foreach (var price in relevantPrices)
            {
                performance.HighPrice = Math.Max(performance.HighPrice, (float)price.Price);
                performance.LowPrice = Math.Min(performance.LowPrice, (float)price.Price);

                // Check for stop loss
                if ((isLongTrade && price.Price <= (decimal)signal.Stoploss) ||
                    (!isLongTrade && price.Price >= (decimal)signal.Stoploss))
                {
                    await SendStopLossMessage(performance, signal, price.Price);
                    CloseSignal(performance, signal, "Stoploss Hit", price.Price);
                    await _context.SaveChangesAsync();
                    return;
                }

                // Check for take profits
                foreach (var tp in takeProfits)
                {
                    if ((isLongTrade && price.Price >= tp) || (!isLongTrade && price.Price <= tp))
                    {
                        if (!achievedTakeProfits.Contains(tp))
                        {
                            achievedTakeProfits.Add(tp);

                            if (performance.TakeProfitsAchieved == null)
                                performance.TakeProfitsAchieved = 0;
                            performance.TakeProfitsAchieved++;

                            notifiedTakeProfits.Add(tp);

                            // Update AchievedTakeProfits before sending the message
                            performance.AchievedTakeProfits = string.Join(",", achievedTakeProfits.Select(x => x.ToString(CultureInfo.InvariantCulture)));

                            
                            // Send Take Profit Message
                            await SendTakeProfitMessage(performance, signal, tp);
                        }
                    }
                }

                // Close signal if all take profits are achieved
                if (performance.TakeProfitsAchieved >= performance.TakeProfitCount)
                {
                    CloseSignal(performance, signal, "All Take Profits Achieved", price.Price);
                    return;
                }
            }

            // Always update AchievedTakeProfits and NotifiedTakeProfits at the end
            performance.AchievedTakeProfits = string.Join(",", achievedTakeProfits.Select(tp => tp.ToString(CultureInfo.InvariantCulture)));
            performance.NotifiedTakeProfits = string.Join(",", notifiedTakeProfits.Select(tp => tp.ToString(CultureInfo.InvariantCulture)));

            try
            {
                // Save changes to DB
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                using (var errorLogScope = _scopeFactory.CreateScope())
                {
                    var errorLogService = errorLogScope.ServiceProvider.GetRequiredService<ErrorLogService>();
                    await errorLogService.LogErrorAsync(
                        $"Failed to Track signal performance",
                        ex.StackTrace, "SignalPerformanceService.HandleOpen", $"Inner Ex: {ex.InnerException}");
                }
            }
        }

        private async void CloseSignal(SignalPerformance performance, Signal signal, string reason, decimal closingPrice)
        {
            performance.Status = "Closed";
            performance.EndTime = DateTime.Now;
            performance.Notes = reason;
            performance.ProfitLoss = CalculateProfitLoss(signal, closingPrice);

            try {                 
                _context.SignalPerformances.Update(performance);
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                using (var errorLogScope = _scopeFactory.CreateScope())
                {
                    var errorLogService = errorLogScope.ServiceProvider.GetRequiredService<ErrorLogService>();
                    await errorLogService.LogErrorAsync(
                        $"Failed to Track signal performance",
                        ex.StackTrace, "SignalPerformanceService.CloseSignal", $"Inner Ex: {ex.InnerException}");
                }
            }

        }

        private async Task SendTakeProfitMessage(SignalPerformance performance, Signal signal, decimal takeProfitLevel)
        {

            var promoButtons = new List<IEnumerable<InlineKeyboardButton>>
            {
                new[]
                {
                    InlineKeyboardButton.WithUrl("💸 Exchanges", "https://autosignals.xyz/Exchanges"),
                    InlineKeyboardButton.WithUrl("📊 Analytics", "https://autosignals.xyz/Providers")
                },
                new[]
                {
                    InlineKeyboardButton.WithUrl("🔑 Free Sign-up", "https://autosignals.xyz/Identity/Account/Login"),
                }

            };

            var duration = DateTime.Now - performance.StartTime;
            var message = $"""
<b>Take Profit Achieved 🎉</b> 
<i>{GetPraiseMessage()} 🎉</i>

<b>Provider:</b> {signal.Provider}

<b>Signal ID:</b> {signal.Id}
<b>Symbol:</b> {signal.Symbol}  
<b>Side:</b> {signal.Side}  
<b>Take Profit Level:</b> {takeProfitLevel}  
<b>Duration:</b> {duration.Days}d {duration.Hours}h {duration.Minutes}m  
<b>Profit Percentage:</b> {CalculateProfitPercentage(signal, takeProfitLevel)}% 
<b>Take Profits Achieved: </b> {performance.AchievedTakeProfits}
<b>Take Profit </b> {performance.TakeProfitsAchieved} <b> of {performance.TakeProfitCount}</b>

<b>Trading Bot: @CL_AutoSignals_Bot</b>
""";

            int? replyToMessageId = null;
            if (int.TryParse(performance.TelegramMessageId, out var msgId))
                replyToMessageId = msgId;

            await _telegramBotService.PostMessageToGroupAsync(
                message,
                CancellationToken.None,
                replyToMessageId: replyToMessageId,
                null, // No message thread ID needed for take profit messages
                null, // No image stream needed
                null, // No image file name needed
                promoButtons
            );
        }

        private async Task SendStopLossMessage(SignalPerformance performance, Signal signal, decimal stopLossPrice)
        {
            var riskButtons = new List<IEnumerable<InlineKeyboardButton>>
            {
                new[]
                {
                    InlineKeyboardButton.WithUrl("📚 Education", "https://autosignals.xyz/education/basics"),
                    InlineKeyboardButton.WithUrl("🛡️ Risk Management", "https://autosignals.xyz/education/risk-management")
                },
                new[]
                {
                    InlineKeyboardButton.WithUrl("📊 Analytics", "https://autosignals.xyz/Providers"),
                }
            };

            var duration = DateTime.Now - performance.StartTime;
            var message = $"""
<b>Stop-Loss Hit ⚠️</b>

<b>Provider:</b> {signal.Provider}

<b>Signal ID:</b> {signal.Id}
<b>Symbol:</b> {signal.Symbol}  
<b>Side:</b> {signal.Side}  
<b>Entry:</b> {signal.Entry}  
<b>Stop-Loss:</b> {signal.Stoploss}   
<b>Duration:</b> {duration.Days}d {duration.Hours}h {duration.Minutes}m  
<b>Reached </b> {performance.TakeProfitsAchieved} <b> of {performance.TakeProfitCount}</b> TakeProfits 

<i>{GetEncouragingMessage()}</i>

<b>⚠️ Trading is NOT risk free</b>
<b>⚠️ Don't trade what you can't lose</b>
<b>⚠️ Manage your risk</b>
""";

            int? replyToMessageId = null;
            if (int.TryParse(performance.TelegramMessageId, out var msgId))
                replyToMessageId = msgId;

            await _telegramBotService.PostMessageToGroupAsync(
                message,
                CancellationToken.None,
                replyToMessageId: replyToMessageId,
                null, // No message thread ID needed for stop loss messages
                null, // No image stream needed
                null, // No image file name needed
                riskButtons
            );
        }

        // Replace the async List<decimal> ParseTakeProfits(string takeProfits) method with Task<List<decimal>> as the return type
        private async Task<List<decimal>> ParseTakeProfits(string takeProfits)
        {
            try
            {
                return takeProfits
                                .Split(',')
                                .Select(tp => tp.Trim())
                                .Where(tp => !string.IsNullOrEmpty(tp))
                                .Select(tp => decimal.Parse(tp, NumberStyles.Any, CultureInfo.InvariantCulture))
                                .ToList();
            }
            catch (FormatException ex)
            {
                using (var errorLogScope = _scopeFactory.CreateScope())
                {
                    var errorLogService = errorLogScope.ServiceProvider.GetRequiredService<ErrorLogService>();
                    await errorLogService.LogErrorAsync(
                        $"Failed to parse take profits: {takeProfits}",
                        ex.StackTrace, "SignalPerformanceService.ParseTakeProfits", $"Inner Ex: {ex.InnerException}");
                }
                return new List<decimal>();
            }
        }

        private float CalculateProfitLoss(Signal signal, decimal currentPrice)
        {
            var entryPrice = signal.Entry;
            bool isLongTrade = signal.Side.Equals("long", StringComparison.OrdinalIgnoreCase);

            if (isLongTrade)
            {
                return (float)Math.Round((((currentPrice - (decimal)entryPrice) / (decimal)entryPrice) * 100), 2);
            }
            else // Short trade
            {
                return (float)Math.Round(((((decimal)entryPrice - currentPrice) / (decimal)entryPrice) * 100), 2);
            }
        }

    }
}
