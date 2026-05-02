using AutoSignals.Models;
using AutoSignals.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Telegram.Bot.Types.ReplyMarkups;

namespace AutoSignals.Services
{
    public class SignalPerformanceService
    {
        private readonly AutoSignalsDbContext _context;
        private readonly ITelegramNotifier _telegramNotifier;
        private readonly IWebHostEnvironment _env;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ErrorLogService _errorLogService;

        public SignalPerformanceService(AutoSignalsDbContext context, ITelegramNotifier telegramNotifier, IServiceScopeFactory scopeFactory, IWebHostEnvironment env, ErrorLogService errorLogService)
        {
            _context = context;
            _telegramNotifier = telegramNotifier ?? throw new ArgumentNullException(nameof(telegramNotifier));
            _scopeFactory = scopeFactory;
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _errorLogService = errorLogService;
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

        public async Task<Image> RenderSignalImageAsync(
            string text,
            string logoPath,
            Signal signal,
            int width = 1000,
            int padding = 40)
        {
            var candles = await FetchCandlesAsync(signal.Symbol);
            var tps     = await ParseTakeProfits(signal.TakeProfits);

            var font           = new Font("Segoe UI", 40, FontStyle.Bold,   GraphicsUnit.Pixel);
            var watermarkFont  = new Font("Segoe UI", 28, FontStyle.Italic, GraphicsUnit.Pixel);
            var watermarkBrush = new SolidBrush(Color.FromArgb(20, 255, 255, 255));

            var textAreaWidth = width - 2 * padding;
            using var tempBmp = new Bitmap(width, 1);
            using var tempGfx = Graphics.FromImage(tempBmp);
            var textSize = tempGfx.MeasureString(text, font, textAreaWidth);

            var   logo      = Image.FromFile(logoPath);
            float aspect    = (float)logo.Width / logo.Height;
            int   logoH     = (int)(width / aspect);
            const int chartH   = 360;
            const int spacing  = 16;

            int totalHeight = logoH + spacing + chartH + spacing + (int)textSize.Height + padding;

            var finalImage = new Bitmap(width, totalHeight);
            using var g = Graphics.FromImage(finalImage);
            g.Clear(Color.FromArgb(18, 18, 18));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode     = SmoothingMode.AntiAlias;

            // Watermark tile
            var wmText = " AutoSignals.xyz • @CL_AutoSignals_Bot ";
            for (int wy = -totalHeight; wy < totalHeight * 2; wy += 100)
                for (int wx = -width; wx < width * 2; wx += 250)
                {
                    g.TranslateTransform(wx, wy);
                    g.RotateTransform(-30);
                    g.DrawString(wmText, watermarkFont, watermarkBrush, 0, 0);
                    g.ResetTransform();
                }

            // Logo
            g.DrawImage(logo, new Rectangle(0, 0, width, logoH));
            logo.Dispose();

            // Candlestick chart
            int chartY = logoH + spacing;
            DrawCandleChart(g, new Rectangle(padding, chartY, width - 2 * padding, chartH), candles, signal, tps);

            // Signal text with drop shadow
            float textY = chartY + chartH + spacing;
            using (var shadowBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.DrawString(text, font, shadowBrush, padding + 2f, textY + 2f);
            g.DrawString(text, font, Brushes.White, (float)padding, textY);

            font.Dispose();
            watermarkFont.Dispose();
            watermarkBrush.Dispose();
            return finalImage;
        }

        private static void DrawCandleChart(
            Graphics g,
            Rectangle chartRect,
            List<CandleDto> candles,
            Signal signal,
            List<decimal> takeProfits)
        {
            using var bgBrush = new SolidBrush(Color.FromArgb(22, 22, 35));
            g.FillRectangle(bgBrush, chartRect);

            using var gridFont  = new Font("Segoe UI", 16, FontStyle.Regular, GraphicsUnit.Pixel);
            using var levelFont = new Font("Segoe UI", 15, FontStyle.Bold,    GraphicsUnit.Pixel);

            if (candles == null || candles.Count == 0)
            {
                using var noDataBrush = new SolidBrush(Color.FromArgb(100, 255, 255, 255));
                g.DrawString("No 4H chart data available", gridFont, noDataBrush,
                    chartRect.X + 20f, chartRect.Y + chartRect.Height / 2f - 10f);
                using var borderPen0 = new Pen(Color.FromArgb(50, 255, 255, 255), 1);
                g.DrawRectangle(borderPen0, chartRect);
                return;
            }

            // Price range: envelope candles AND all signal levels
            var allLevels = new List<decimal> { (decimal)signal.Entry, (decimal)signal.Stoploss };
            allLevels.AddRange(takeProfits);
            decimal minPrice = Math.Min(candles.Min(c => c.Low),  allLevels.Min());
            decimal maxPrice = Math.Max(candles.Max(c => c.High), allLevels.Max());
            decimal range    = maxPrice - minPrice;
            if (range == 0) range = maxPrice * 0.01m;
            minPrice -= range * 0.05m;
            maxPrice += range * 0.10m;
            range = maxPrice - minPrice;

            // Candle drawing area (left 75%) + projection area (right 25%) + label margin
            const int   labelMargin     = 155;
            const float projRatio       = 0.25f;
            int         workingWidth    = chartRect.Width - labelMargin;
            int         projectionWidth = (int)(workingWidth * projRatio);
            int         candleWidth     = workingWidth - projectionWidth;
            var candleArea     = new Rectangle(chartRect.X,              chartRect.Y, candleWidth,     chartRect.Height);
            var projectionArea = new Rectangle(chartRect.X + candleWidth, chartRect.Y, projectionWidth, chartRect.Height);

            float PriceToY(decimal price) =>
                candleArea.Bottom - (float)((price - minPrice) / range) * candleArea.Height;

            // Projection area: subtle tint + separator (drawn before grid so grid appears on top)
            using (var projBg = new SolidBrush(Color.FromArgb(12, 255, 255, 255)))
                g.FillRectangle(projBg, projectionArea);
            using (var sepPen = new Pen(Color.FromArgb(55, 255, 255, 255), 1))
                g.DrawLine(sepPen, projectionArea.X, projectionArea.Y, projectionArea.X, projectionArea.Bottom);

            // Horizontal grid + right-side price axis labels (spanning full working width)
            using var gridPen   = new Pen(Color.FromArgb(28, 255, 255, 255), 1);
            using var gridBrush = new SolidBrush(Color.FromArgb(65, 255, 255, 255));
            for (int i = 0; i <= 5; i++)
            {
                float   gy     = candleArea.Y + (float)i / 5 * candleArea.Height;
                decimal gPrice = maxPrice - range * i / 5;
                g.DrawLine(gridPen, candleArea.X, gy, projectionArea.Right, gy);
                g.DrawString(FormatPrice(gPrice), gridFont, gridBrush, projectionArea.Right + 4, gy - 9);
            }

            // Position zone fills in projection area (TradingView-style: red = risk, green = reward)
            bool  isLong  = signal.Side.Equals("long", StringComparison.OrdinalIgnoreCase);
            float entryY  = PriceToY((decimal)signal.Entry);
            float slY     = PriceToY((decimal)signal.Stoploss);
            using (var redZone = new SolidBrush(Color.FromArgb(75, 239, 83, 80)))
                g.FillRectangle(redZone, projectionArea.X, Math.Min(entryY, slY), projectionArea.Width, Math.Abs(entryY - slY));
            if (takeProfits.Count > 0)
            {
                // Sort TPs in trade direction so segments run entry→TP1→TP2→…→TPn
                var sortedTps = isLong
                    ? takeProfits.OrderBy(tp => tp).ToList()
                    : takeProfits.OrderByDescending(tp => tp).ToList();

                var levels = new List<decimal> { (decimal)signal.Entry };
                levels.AddRange(sortedTps);

                using var greenZone = new SolidBrush(Color.FromArgb(75, 38, 166, 154));
                for (int i = 0; i < levels.Count - 1; i++)
                {
                    float y1 = PriceToY(levels[i]);
                    float y2 = PriceToY(levels[i + 1]);
                    g.FillRectangle(greenZone, projectionArea.X, Math.Min(y1, y2), projectionArea.Width, Math.Abs(y1 - y2));
                }
            }

            // Gold entry arrow at the candle/projection boundary pointing right
            using (var arrowBrush = new SolidBrush(Color.FromArgb(245, 166, 35)))
                g.FillPolygon(arrowBrush, new PointF[] {
                    new(projectionArea.X,        entryY - 7f),
                    new(projectionArea.X,        entryY + 7f),
                    new(projectionArea.X + 12f,  entryY)
                });

            // Candles
            int   count = candles.Count;
            float slotW = (float)candleArea.Width / count;
            float bodyW = Math.Max(2f, slotW * 0.65f);

            // Pre-create bull/bear resources to avoid per-candle allocation overhead
            using var bullPen   = new Pen(Color.FromArgb(38, 166, 154), 1.5f);
            using var bullBrush = new SolidBrush(Color.FromArgb(38, 166, 154));
            using var bearPen   = new Pen(Color.FromArgb(239, 83, 80), 1.5f);
            using var bearBrush = new SolidBrush(Color.FromArgb(239, 83, 80));

            for (int i = 0; i < count; i++)
            {
                var   c      = candles[i];
                float cx     = candleArea.X + i * slotW + slotW / 2f;
                float openY  = PriceToY(c.Open);
                float closeY = PriceToY(c.Close);
                float highY  = PriceToY(c.High);
                float lowY   = PriceToY(c.Low);
                bool  bull   = c.Close >= c.Open;

                var wickPen   = bull ? bullPen   : bearPen;
                var bodyBrush = bull ? bullBrush : bearBrush;

                g.DrawLine(wickPen, cx, highY, cx, lowY);
                float bodyTop = Math.Min(openY, closeY);
                float bodyH   = Math.Max(1f, Math.Abs(openY - closeY));
                g.FillRectangle(bodyBrush, cx - bodyW / 2f, bodyTop, bodyW, bodyH);
            }

            // Signal level lines with label pills
            void DrawLevel(decimal price, Color color, string label, bool dashed)
            {
                float y = PriceToY(price);
                if (y < candleArea.Y - 1 || y > candleArea.Bottom + 1) return;

                using var linePen = new Pen(color, 2f);
                if (dashed) linePen.DashStyle = DashStyle.Dash;
                g.DrawLine(linePen, candleArea.X, y, projectionArea.Right, y);

                var   labelText = $"{label} {FormatPrice(price)}";
                var   sz        = g.MeasureString(labelText, levelFont);
                float lx        = projectionArea.Right + 4;
                float ly        = y - sz.Height / 2f;
                using var pillBrush = new SolidBrush(Color.FromArgb(210, color.R, color.G, color.B));
                g.FillRectangle(pillBrush, lx - 3, ly - 2, sz.Width + 6, sz.Height + 4);
                g.DrawString(labelText, levelFont, Brushes.White, lx, ly);
            }

            DrawLevel((decimal)signal.Entry,    Color.FromArgb(245, 166,  35), "Entry", false);
            DrawLevel((decimal)signal.Stoploss, Color.FromArgb(239,  83,  80), "SL",    true);
            for (int i = 0; i < takeProfits.Count; i++)
                DrawLevel(takeProfits[i], Color.FromArgb(38, 166, 154), $"TP{i + 1}", true);

            // Interval badge (top-left corner)
            using var tagBg   = new SolidBrush(Color.FromArgb(160, 22, 22, 35));
            using var tagFont = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            using var tagFg   = new SolidBrush(Color.FromArgb(180, 255, 255, 255));
            g.FillRectangle(tagBg, chartRect.X + 6, chartRect.Y + 5, 44, 22);
            g.DrawString("4H", tagFont, tagFg, chartRect.X + 9f, chartRect.Y + 5f);

            // Chart border
            using var borderPen = new Pen(Color.FromArgb(50, 255, 255, 255), 1);
            g.DrawRectangle(borderPen, chartRect);
        }

        private async Task<List<CandleDto>> FetchCandlesAsync(string symbol, string type = "swap", int limit = 60)
        {
            const int intervalMinutes = 240; // 4 h
            var since = DateTime.UtcNow.AddMinutes(-(long)intervalMinutes * limit);
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var snapshots = await _context.KLineAssetPrices
                .AsNoTracking()
                .Where(k => k.Symbol == symbol && k.Type == type && k.Time >= since)
                .OrderBy(k => k.Time)
                .Select(k => new { k.Time, k.Price, k.Open, k.High, k.Low, k.Close, k.Volume })
                .ToListAsync();

            if (snapshots.Count == 0)
                return new List<CandleDto>();

            return snapshots
                .GroupBy(s => FloorToInterval(s.Time, intervalMinutes, epoch))
                .OrderBy(grp => grp.Key)
                .Select(grp =>
                {
                    var prices = grp.Select(s => s.Price).ToList();
                    return new CandleDto
                    {
                        Time   = (long)(grp.Key - epoch).TotalSeconds,
                        Open   = prices.First(),
                        High   = prices.Max(),
                        Low    = prices.Min(),
                        Close  = prices.Last(),
                        Volume = grp.Sum(s => s.Volume)
                    };
                })
                .TakeLast(limit)
                .ToList();
        }

        private static DateTime FloorToInterval(DateTime dt, int intervalMinutes, DateTime epoch)
        {
            long total   = (long)(dt.ToUniversalTime() - epoch).TotalMinutes;
            long floored = total / intervalMinutes * intervalMinutes;
            return epoch.AddMinutes(floored);
        }

        private static string FormatPrice(decimal price) =>
            price >= 10_000 ? price.ToString("N0") :
            price >= 1      ? price.ToString("N2") :
            price >= 0.001m ? price.ToString("N4") :
                              price.ToString("N6").TrimEnd('0').TrimEnd('.');

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
            return messages[Random.Shared.Next(messages.Count)];
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
            return messages[Random.Shared.Next(messages.Count)];
        }

        public async Task TrackPerformance()
        {
            var now = DateTime.UtcNow;
            var signalPerformances = await _context.SignalPerformances
                .Where(s => s.Status == "Open" || s.Status == "Pending")
                .ToListAsync();
            var signals = await _context.Signals.ToListAsync();

            if (signalPerformances.Count == 0)
                return;

            var activeSymbols = signalPerformances
                .Select(sp => signals.FirstOrDefault(s => s.Id == sp.SignalId)?.Symbol)
                .Where(s => s != null)
                .Distinct()
                .ToList();
            var earliestStartTime = signalPerformances.Min(sp => sp.StartTime);
            var priceData = await _context.GeneralAssetPrices
                .Where(p => activeSymbols.Contains(p.Symbol) && p.Time >= earliestStartTime)
                .ToListAsync();

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
                await _errorLogService.LogErrorAsync(
                    $"Failed to Track signal performance",
                    ex.StackTrace, "SignalPerformanceService", $"Inner Ex: {ex.InnerException}");
            }

            var end = DateTime.UtcNow;
            var duration = end - now;
            System.Console.WriteLine($"Signal performance tracking completed in {duration.TotalSeconds} seconds.");
        }

        private async Task HandlePendingSignal(SignalPerformance performance, Signal signal, List<GeneralAssetPrice> relevantPrices)
        {
            var currentTime = DateTime.UtcNow;

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
                performance.StartTime = currentTime;

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
                var image = await RenderSignalImageAsync(messageText, logoPath, signal);
                using var stream = new MemoryStream();
                image.Save(stream, ImageFormat.Png);

                // Build caption — include AI narrative if available
                var prediction = await _context.SignalPredictions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.SignalId == signal.Id);
                var caption = "🚀 New Trade Signal";
                if (!string.IsNullOrWhiteSpace(prediction?.NarrativeAnalysis))
                    caption += $"\n\n<i>🤖 {prediction.NarrativeAnalysis}</i>";

                // Send image
                var msgId = await _telegramNotifier.PostMessageToGroupAsync(
                    message: caption,
                    cancellationToken: CancellationToken.None,
                    replyToMessageId: null,
                    messageThreadId: null, // <-- Topic ID here
                    imageStream: stream,
                    imageFileName: "new_trade.png",
                    buttons: buttons
                );

                performance.TelegramMessageId = msgId?.ToString(); // Save the Telegram message ID
            }
            else if ((currentTime - performance.StartTime).TotalHours > 24)
            {
                performance.Status = "Canceled";
                performance.Notes = "Entry price not reached within 24 hours";
                performance.EndTime = currentTime;

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
                    await CloseSignal(performance, signal, "Stoploss Hit", price.Price);
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
                    await CloseSignal(performance, signal, "All Take Profits Achieved", price.Price);
                    return;
                }
            }

            // Always update AchievedTakeProfits and NotifiedTakeProfits at the end
            performance.AchievedTakeProfits = string.Join(",", achievedTakeProfits.Select(tp => tp.ToString(CultureInfo.InvariantCulture)));
            performance.NotifiedTakeProfits = string.Join(",", notifiedTakeProfits.Select(tp => tp.ToString(CultureInfo.InvariantCulture)));
        }

        private async Task CloseSignal(SignalPerformance performance, Signal signal, string reason, decimal closingPrice)
        {
            var currentTime = DateTime.UtcNow;

            performance.Status = "Closed";
            performance.EndTime = currentTime;
            performance.Notes = reason;
            performance.ProfitLoss = CalculateProfitLoss(signal, closingPrice);
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

            var duration = DateTime.UtcNow - performance.StartTime;
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

            await _telegramNotifier.PostMessageToGroupAsync(
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

            var duration = DateTime.UtcNow - performance.StartTime;
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

            await _telegramNotifier.PostMessageToGroupAsync(
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
                await _errorLogService.LogErrorAsync(
                    $"Failed to parse take profits: {takeProfits}",
                    ex.StackTrace, "SignalPerformanceService.ParseTakeProfits", $"Inner Ex: {ex.InnerException}");
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
