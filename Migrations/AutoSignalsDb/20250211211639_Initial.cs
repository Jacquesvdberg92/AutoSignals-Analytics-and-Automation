using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BinanceAssetPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinanceAssetPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BinanceMarkets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuoteCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinTradeUSDT = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinLever = table.Column<int>(type: "int", nullable: false),
                    MaxLever = table.Column<int>(type: "int", nullable: false),
                    PricePrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    AmountPrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinanceMarkets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BinanceRemovedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinanceRemovedAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BitgetAssetPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BitgetAssetPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BitgetMarkets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuoteCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinTradeUSDT = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinLever = table.Column<int>(type: "int", nullable: false),
                    MaxLever = table.Column<int>(type: "int", nullable: false),
                    PricePrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    AmountPrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BitgetMarkets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BitgetRemovedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BitgetRemovedAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BybitAssetPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BybitAssetPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BybitMarkets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuoteCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinTradeUSDT = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinLever = table.Column<int>(type: "int", nullable: false),
                    MaxLever = table.Column<int>(type: "int", nullable: false),
                    PricePrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    AmountPrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BybitMarkets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BybitRemovedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BybitRemovedAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Exchanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Referal = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferalClicked = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exchanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneralAssetPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralAssetPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KuCoinAssetPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KuCoinAssetPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KuCoinMarkets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuoteCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinTradeUSDT = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinLever = table.Column<int>(type: "int", nullable: false),
                    MaxLever = table.Column<int>(type: "int", nullable: false),
                    PricePrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    AmountPrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KuCoinMarkets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KuCoinRemovedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KuCoinRemovedAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OkxAssetPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OkxAssetPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OkxMarkets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuoteCoin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TakerFeeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinTradeUSDT = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MinLever = table.Column<int>(type: "int", nullable: false),
                    MaxLever = table.Column<int>(type: "int", nullable: false),
                    PricePrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    AmountPrecision = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OkxMarkets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OkxRemovedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OkxRemovedAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SignalId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TelegramId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PositionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<double>(type: "float", nullable: true),
                    Stoploss = table.Column<double>(type: "float", nullable: true),
                    Size = table.Column<double>(type: "float", nullable: false),
                    Leverage = table.Column<double>(type: "float", nullable: false),
                    IsIsolated = table.Column<bool>(type: "bit", nullable: false),
                    IsTest = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TelegramId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Leverage = table.Column<int>(type: "int", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Entry = table.Column<double>(type: "float", nullable: false),
                    Stoploss = table.Column<double>(type: "float", nullable: false),
                    ROI = table.Column<double>(type: "float", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Provider",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RRR = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AverageProfitPerTrade = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StoplossPersentage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignalCount = table.Column<int>(type: "int", nullable: true),
                    AverageLeverage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TakeProfitTargets = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignalsNullified = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TradeStyle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TradesPerDay = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TradeTimeframes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AverageWinRate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LongWinRate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShortWinRate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LongCount = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShortCount = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TpAchieved = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Risk = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TakeProfitDistribution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Telegram = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Picture = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Provider", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProvidersSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    OverideLeverage = table.Column<bool>(type: "bit", nullable: false),
                    Leverage = table.Column<int>(type: "int", nullable: false),
                    UseStoploss = table.Column<bool>(type: "bit", nullable: false),
                    StoplossPercentage = table.Column<double>(type: "float", nullable: false),
                    MoveStoploss = table.Column<bool>(type: "bit", nullable: false),
                    MoveStoplossOn = table.Column<int>(type: "int", nullable: false),
                    RiskPercentage = table.Column<double>(type: "float", nullable: false),
                    MaxTradeSizeUsd = table.Column<double>(type: "float", nullable: false),
                    MinTradeSizeUsd = table.Column<double>(type: "float", nullable: false),
                    IsIsolated = table.Column<bool>(type: "bit", nullable: false),
                    UseMoonbag = table.Column<bool>(type: "bit", nullable: false),
                    MoonbagPercentage = table.Column<int>(type: "int", nullable: false),
                    MoonbagSize = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvidersSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignalPerformances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignalId = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HighPrice = table.Column<float>(type: "real", nullable: false),
                    LowPrice = table.Column<float>(type: "real", nullable: false),
                    ProfitLoss = table.Column<float>(type: "real", nullable: true),
                    TakeProfitCount = table.Column<int>(type: "int", nullable: false),
                    TakeProfitsAchieved = table.Column<int>(type: "int", nullable: true),
                    AchievedTakeProfits = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NotifiedTakeProfits = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalPerformances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Signals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Leverage = table.Column<int>(type: "int", nullable: false),
                    Entry = table.Column<float>(type: "real", nullable: false),
                    Stoploss = table.Column<float>(type: "real", nullable: false),
                    TakeProfits = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsersData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NickName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TelegramId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TelegramNotifications = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExchangeId = table.Column<int>(type: "int", nullable: true),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiPassword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiTestResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    X = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Instagram = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Facebook = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartBalance = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubscriptionActive = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsersData", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BinanceAssetPrices_Symbol",
                table: "BinanceAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BitgetAssetPrices_Symbol",
                table: "BitgetAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BybitAssetPrices_Symbol",
                table: "BybitAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralAssetPrices_Symbol",
                table: "GeneralAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KuCoinAssetPrices_Symbol",
                table: "KuCoinAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OkxAssetPrices_Symbol",
                table: "OkxAssetPrices",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BinanceAssetPrices");

            migrationBuilder.DropTable(
                name: "BinanceMarkets");

            migrationBuilder.DropTable(
                name: "BinanceRemovedAssets");

            migrationBuilder.DropTable(
                name: "BitgetAssetPrices");

            migrationBuilder.DropTable(
                name: "BitgetMarkets");

            migrationBuilder.DropTable(
                name: "BitgetRemovedAssets");

            migrationBuilder.DropTable(
                name: "BybitAssetPrices");

            migrationBuilder.DropTable(
                name: "BybitMarkets");

            migrationBuilder.DropTable(
                name: "BybitRemovedAssets");

            migrationBuilder.DropTable(
                name: "Exchanges");

            migrationBuilder.DropTable(
                name: "GeneralAssetPrices");

            migrationBuilder.DropTable(
                name: "KuCoinAssetPrices");

            migrationBuilder.DropTable(
                name: "KuCoinMarkets");

            migrationBuilder.DropTable(
                name: "KuCoinRemovedAssets");

            migrationBuilder.DropTable(
                name: "OkxAssetPrices");

            migrationBuilder.DropTable(
                name: "OkxMarkets");

            migrationBuilder.DropTable(
                name: "OkxRemovedAssets");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "Provider");

            migrationBuilder.DropTable(
                name: "ProvidersSettings");

            migrationBuilder.DropTable(
                name: "SignalPerformances");

            migrationBuilder.DropTable(
                name: "Signals");

            migrationBuilder.DropTable(
                name: "UsersData");
        }
    }
}
