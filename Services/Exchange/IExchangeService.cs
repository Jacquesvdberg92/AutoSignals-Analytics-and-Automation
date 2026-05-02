namespace AutoSignals.Services
{
	using System.Collections.Generic;
	using System.Threading.Tasks;

	public interface IExchangeService
	{
		// Common methods for all exchanges can go here if any
	}

	public interface IBinanceService : IExchangeService
	{
		Task<IEnumerable<object>> GetBinanceMarketsAsync();
		//Task FetchAllBinanceAssetPricesAsync(); // Old method
        Task FetchAllBinanceAssetPricesV2Async();
        Task GetTickerPricesViaWebSocketAsync();
        Task<decimal?> FetchBinanceAssetPriceAsync(string symbol);
		Task DeleteDuplicates();
	}

	public interface IBitgetService : IExchangeService
	{
		Task<IEnumerable<object>> GetBitgetMarketsAsync();
		//Task FetchAllBitgetAssetPricesAsync(); // Old method
        Task FetchAllBitgetAssetPricesV2Async();
        Task GetTickerPricesViaWebSocketAsync();
        Task<decimal?> FetchBitgetAssetPriceAsync(string symbol);
        Task DeleteDuplicates();

    }

    public interface IBybitService : IExchangeService
    {
        Task<IEnumerable<object>> GetBybitMarketsAsync();
        //Task FetchAllBybitAssetPricesAsync(); // Old method
        Task FetchAllBybitAssetPricesV2Async();
        Task GetTickerPricesViaWebSocketAsync();
        Task<decimal?> FetchBybitAssetPriceAsync(string symbol);
        Task DeleteDuplicates();
    }

    public interface IOkxService : IExchangeService
    {
        Task<IEnumerable<object>> GetOkxMarketsAsync();
        //Task FetchAllOkxAssetPricesAsync(); // Old method
        Task FetchAllOkxAssetPricesV2Async();
        Task GetTickerPricesViaWebSocketAsync();
        Task<decimal?> FetchOkxAssetPriceAsync(string symbol);
        Task DeleteDuplicates();
    }

    public interface IKuCoinService : IExchangeService
    {
        Task<IEnumerable<object>> GetKuCoinMarketsAsync();
        //Task FetchAllKuCoinAssetPricesAsync(); // Old method
        Task FetchAllKuCoinAssetPricesV2Async();
        Task GetTickerPricesViaWebSocketAsync();
        Task<decimal?> FetchKuCoinAssetPriceAsync(string symbol);
        Task DeleteDuplicates();
    }
}
