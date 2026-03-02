using AutoSignals.Models;

namespace AutoSignals.ViewModels;

public sealed class AssetHoldingsModalViewModel
{
    public int PortfolioId { get; set; }
    public string Symbol { get; set; } = string.Empty;

    public decimal SpotPrice { get; set; }
    public GeneralAssetPrice? LatestCandle { get; set; }

    public List<PortfolioHolding> Lots { get; set; } = new();

    public decimal TotalQuantity => Lots.Sum(l => l.Quantity);
    public decimal WeightedAverageBuyPrice =>
        TotalQuantity > 0 ? Lots.Sum(l => l.Quantity * l.AverageBuyPrice) / TotalQuantity : 0m;

    public decimal TotalCostBasis => Lots.Sum(l => l.Quantity * l.AverageBuyPrice);
    public decimal TotalValue => TotalQuantity * SpotPrice;
    public decimal TotalPnL => TotalValue - TotalCostBasis;
    public decimal TotalPnLPercentage => TotalCostBasis > 0 ? (TotalPnL / TotalCostBasis) * 100m : 0m;
}