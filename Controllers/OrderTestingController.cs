using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using AutoSignals.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class OrderTestingController : Controller
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ErrorLogService _errorLogService;

        public OrderTestingController(IServiceScopeFactory scopeFactory, ErrorLogService errorLogService)
        {
            _scopeFactory = scopeFactory;
            _errorLogService = errorLogService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var vm = new TestOrderViewModel
            {
                Exchange = "BITGET",
                Symbol = "BTC/USDT:USDT",
                Direction = "buy",
                Leverage = 1,
                IsIsolated = true,
                Status = "OPEN"
            };

            return View("~/Views/TestingDevelopment/Index.cshtml", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(TestOrderViewModel vm)
        {
            vm.Symbol = "BTC/USDT:USDT";
            vm.Status = "OPEN";

            try
            {
                var exchangeId = NormalizeExchange(vm.Exchange);

                var order = new Order
                {
                    UserId = "TEST",
                    ExchangeId = exchangeId.ToString(),
                    Symbol = vm.Symbol,
                    Side = vm.Direction,
                    Price = vm.Price,
                    Stoploss = vm.Stoploss,
                    Size = vm.Size,
                    Leverage = vm.Leverage,
                    Status = "OPEN",
                    Description = vm.Description,
                    IsTest = true
                };

                var orderType = GetOrderType(vm.Description);

                switch (exchangeId)
                {
                    case 1:
                        {
                            var bitget = new BitgetPriceService(vm.ApiKey ?? "", vm.ApiSecret ?? "", vm.Password ?? "", _errorLogService, _scopeFactory);

                            switch (orderType)
                            {
                                case TestOrderType.Entry:
                                    {
                                        var result = await bitget.SendEntryOrderAsync(order, vm.ApiKey ?? "", vm.ApiSecret ?? "", vm.Password ?? "");
                                        // Attempt to get an executed price (fallback to user-entered Price)
                                        decimal executedPrice = (decimal)(order.Price ?? 0);
                                        var livePrice = await bitget.FetchBitgetAssetPriceAsync(order.Symbol);
                                        if (livePrice.HasValue)
                                            executedPrice = livePrice.Value;

                                        if (result != null && result.Success)
                                        {
                                            var positionId = await CreateOrUpdateTestPositionAsync(order, executedPrice);
                                            vm.PositionId = positionId.ToString();
                                            vm.ResponseJson = JsonConvert.SerializeObject(new
                                            {
                                                ExchangeResponse = result.Response,
                                                PositionId = positionId,
                                                Message = "Entry processed and test position updated."
                                            }, Formatting.Indented);
                                        }
                                        else
                                        {
                                            vm.ResponseJson = JsonConvert.SerializeObject(new
                                            {
                                                Error = result?.ErrorMessage ?? "Unknown error",
                                                Code = result?.ErrorCode,
                                                Message = "Entry order failed; position not updated."
                                            }, Formatting.Indented);
                                        }
                                    }
                                    break;

                                case TestOrderType.TakeProfit:
                                    {
                                        // TP requires PositionId – either provided manually or from prior entry
                                        var tpResult = await bitget.SendTakeProfitOrderAsync(order, vm.ApiKey ?? "", vm.ApiSecret ?? "", vm.Password ?? "");
                                        vm.ResponseJson = tpResult ?? "No response";
                                    }
                                    break;

                                case TestOrderType.Stoploss:
                                    {
                                        var slResult = await bitget.SendStoplossOrderAsync(order, vm.ApiKey ?? "", vm.ApiSecret ?? "", vm.Password ?? "");
                                        vm.ResponseJson = slResult ?? "No response";
                                    }
                                    break;
                            }
                        }
                        break;

                    case 2:
                        {
                            var okx = new OkxPriceService(vm.ApiKey ?? "", vm.ApiSecret ?? "", vm.Password ?? "", _errorLogService, _scopeFactory);

                            switch (orderType)
                            {
                                case TestOrderType.Entry:
                                    {
                                        var result = await okx.SendEntryOrderAsync(order, vm.ApiKey ?? "", vm.ApiSecret ?? "", vm.Password ?? "");
                                        decimal executedPrice = (decimal)(order.Price ?? 0);
                                        var livePrice = await okx.FetchOkxAssetPriceAsync(order.Symbol);
                                        if (livePrice.HasValue)
                                            executedPrice = livePrice.Value;

                                        if (result != null && result.Success)
                                        {
                                            var positionId = await CreateOrUpdateTestPositionAsync(order, executedPrice);
                                            vm.PositionId = positionId.ToString();
                                            vm.ResponseJson = JsonConvert.SerializeObject(new
                                            {
                                                ExchangeResponse = result.Response,
                                                PositionId = positionId,
                                                Message = "Entry processed and test position updated (OKX)."
                                            }, Formatting.Indented);
                                        }
                                        else
                                        {
                                            vm.ResponseJson = JsonConvert.SerializeObject(new
                                            {
                                                Error = result?.ErrorMessage ?? "Unknown error",
                                                Code = result?.ErrorCode,
                                                Message = "Entry order failed; position not updated."
                                            }, Formatting.Indented);
                                        }
                                    }
                                    break;

                                case TestOrderType.TakeProfit:
                                    {
                                        var tpResult = await okx.SendTakeProfitOrderAsync(order, vm.ApiKey ?? "", vm.ApiSecret ?? "", vm.Password ?? "");
                                        vm.ResponseJson = tpResult ?? "No response";
                                    }
                                    break;

                                case TestOrderType.Stoploss:
                                    {
                                        var slResult = await okx.SendStoplossOrderAsync(order, vm.ApiKey ?? "", vm.ApiSecret ?? "", vm.Password ?? "");
                                        vm.ResponseJson = slResult ?? "No response";
                                    }
                                    break;
                            }
                        }
                        break;

                    default:
                        vm.ResponseJson = "Unsupported exchange selected.";
                        break;
                }
            }
            catch (Exception ex)
            {
                vm.ResponseJson = $"Error: {ex.Message}";
            }

            return View("~/Views/TestingDevelopment/Index.cshtml", vm);
        }

        // Creates or updates a test position for entry-type test orders.
        // Hardcoded UserId = "TEST" so TP/SL test orders can reference PositionId.
        private async Task<int> CreateOrUpdateTestPositionAsync(Order order, decimal executedPrice)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            // Try to locate existing OPEN position for TEST user
            var existing = await context.Positions
                .FirstOrDefaultAsync(p =>
                    p.UserId == "TEST" &&
                    p.Symbol == order.Symbol &&
                    p.Side == order.Side &&
                    p.Status == "OPEN");

            if (existing != null)
            {
                // Update size
                var currentSize = double.Parse(existing.Size);
                var newSize = currentSize + order.Size;
                // Recalculate average entry
                var totalCost = (currentSize * existing.Entry) + (order.Size * (double)executedPrice);
                existing.Size = newSize.ToString();
                existing.Entry = Math.Round(totalCost / newSize, 8);

                // Update stoploss if present
                if (order.Stoploss.HasValue && order.Stoploss > 0)
                    existing.Stoploss = order.Stoploss.Value;

                existing.EstLiquidation = CalculateEstimatedLiquidation(existing.Entry, existing.Leverage, existing.Side);

                context.Positions.Update(existing);
                await context.SaveChangesAsync();
                order.PositionId = existing.Id.ToString();
                return existing.Id;
            }
            else
            {
                // Create new position
                var position = new Position
                {
                    UserId = "TEST",
                    ExchangeId = order.ExchangeId,
                    TelegramId = "TEST",
                    Side = order.Side,
                    Size = order.Size.ToString(),
                    Leverage = (int)order.Leverage,
                    Symbol = order.Symbol,
                    Entry = (double)executedPrice,
                    Stoploss = order.Stoploss ?? 0,
                    ROI = 0,
                    Status = "OPEN",
                    IsTest = true,
                    Time = DateTime.UtcNow,
                    EstLiquidation = CalculateEstimatedLiquidation((double)executedPrice, (int)order.Leverage, order.Side)
                };

                context.Positions.Add(position);
                await context.SaveChangesAsync();
                order.PositionId = position.Id.ToString();
                return position.Id;
            }
        }

        private double CalculateEstimatedLiquidation(double entryPrice, int leverage, string side)
        {
            if (leverage <= 0) leverage = 1;
            return side.ToLower() switch
            {
                "buy" => Math.Round(entryPrice * (1 - (1.0 / leverage)), 8),
                "sell" => Math.Round(entryPrice * (1 + (1.0 / leverage)), 8),
                _ => entryPrice
            };
        }

        private static int NormalizeExchange(string? exchange)
        {
            if (int.TryParse(exchange, out var id))
                return id;

            var val = (exchange ?? "").Trim().ToUpperInvariant();
            return val switch
            {
                "BITGET" => 1,
                "OKX" => 2,
                _ => 0
            };
        }

        private enum TestOrderType
        {
            Entry,
            TakeProfit,
            Stoploss
        }

        private static TestOrderType GetOrderType(string? description)
        {
            var d = (description ?? "").Trim();

            if (d.Equals("Initial Entry Order", StringComparison.OrdinalIgnoreCase) ||
                d.Equals("DCA1 Entry Order", StringComparison.OrdinalIgnoreCase) ||
                d.Equals("DCA2 Entry Order", StringComparison.OrdinalIgnoreCase))
            {
                return TestOrderType.Entry;
            }

            if (d.Equals("Take Profit Order 1", StringComparison.OrdinalIgnoreCase) ||
                d.Equals("Take Profit Order 1 + MSL", StringComparison.OrdinalIgnoreCase) ||
                d.Equals("Moonbag Order", StringComparison.OrdinalIgnoreCase))
            {
                return TestOrderType.TakeProfit;
            }

            if (d.Equals("Stoploss Order", StringComparison.OrdinalIgnoreCase) ||
                d.Equals("Stop Loss Order", StringComparison.OrdinalIgnoreCase))
            {
                return TestOrderType.Stoploss;
            }

            return TestOrderType.Entry;
        }
    }
}