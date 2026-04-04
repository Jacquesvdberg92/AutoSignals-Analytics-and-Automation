using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AutoSignals.Services
{
    public class AveragePriceService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public AveragePriceService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task CalculateAndSaveAveragePricesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            try
            {
                // Push the entire aggregation to SQL: UNION ALL of all 5 exchange tables,
                // GROUP BY (Symbol, normalized Type), AVG/MAX aggregations, then MERGE upsert.
                await context.Database.ExecuteSqlRawAsync(@"
                    MERGE GeneralAssetPrices AS target
                    USING (
                        SELECT
                            Symbol,
                            CASE
                                WHEN LOWER(LTRIM(RTRIM(Type))) = 'spot'                   THEN 'spot'
                                WHEN LOWER(LTRIM(RTRIM(Type))) IN ('swap', 'perpetual')   THEN 'swap'
                                ELSE LOWER(LTRIM(RTRIM(Type)))
                            END AS Type,
                            AVG(Price)   AS Price,
                            AVG([Open])  AS [Open],
                            AVG(High)    AS High,
                            AVG(Low)     AS Low,
                            AVG([Close]) AS [Close],
                            AVG(Volume)  AS Volume,
                            MAX(Time)    AS Time
                        FROM (
                            SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM BitgetAssetPrices
                            UNION ALL
                            SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM BinanceAssetPrices
                            UNION ALL
                            SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM BybitAssetPrices
                            UNION ALL
                            SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM OkxAssetPrices
                            UNION ALL
                            SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM KuCoinAssetPrices
                        ) AS AllPrices
                        GROUP BY
                            Symbol,
                            CASE
                                WHEN LOWER(LTRIM(RTRIM(Type))) = 'spot'                   THEN 'spot'
                                WHEN LOWER(LTRIM(RTRIM(Type))) IN ('swap', 'perpetual')   THEN 'swap'
                                ELSE LOWER(LTRIM(RTRIM(Type)))
                            END
                    ) AS source ON target.Symbol = source.Symbol AND target.Type = source.Type
                    WHEN MATCHED THEN
                        UPDATE SET
                            Price    = source.Price,
                            [Open]   = source.[Open],
                            High     = source.High,
                            Low      = source.Low,
                            [Close]  = source.[Close],
                            Volume   = source.Volume,
                            Time     = source.Time
                    WHEN NOT MATCHED THEN
                        INSERT (Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time)
                        VALUES (source.Symbol, source.Type, source.Price, source.[Open], source.High,
                                source.Low, source.[Close], source.Volume, source.Time);
                ").ConfigureAwait(false);

                // Remove stale rows not refreshed in the last 24 hours
                await context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM GeneralAssetPrices WHERE Time < {0}",
                    DateTime.UtcNow.AddHours(-24))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating average prices: {ex.Message}");
                Console.WriteLine($"Error Inner Ex: {ex.InnerException}");

                using var errorScope = _scopeFactory.CreateScope();
                var errorLogService = errorScope.ServiceProvider.GetRequiredService<ErrorLogService>();

                await errorLogService.LogErrorAsync(
                    "Failed to save Average Prices",
                    ex.StackTrace,
                    nameof(AveragePriceService),
                    $"Inner Ex: {ex.InnerException}");

                throw;
            }
        }
    }
}
