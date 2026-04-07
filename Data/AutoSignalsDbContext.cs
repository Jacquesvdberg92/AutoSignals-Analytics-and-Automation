using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using AutoSignals.Models;

namespace AutoSignals.Data
{
    public class AutoSignalsDbContext : DbContext
    {
        public AutoSignalsDbContext(DbContextOptions<AutoSignalsDbContext> options) : base(options)
        {
        }
        // Signals
        public DbSet<Signal> Signals { get; set; }
        public DbSet<SignalPerformance> SignalPerformances{ get; set; }
        public DbSet<SignalPrediction> SignalPredictions { get; set; }

        // Orders
        public DbSet<Order> Orders { get; set; }

        // Positions
        public DbSet<Position> Positions { get; set; }

        //Statuses

        // Exchange markets
        public DbSet<BitgetMarket> BitgetMarkets { get; set; }
        public DbSet<BinanceMarket> BinanceMarkets { get; set; }
        public DbSet<BybitMarket> BybitMarkets { get; set; }
        public DbSet<OkxMarket> OkxMarkets { get; set; }
        public DbSet<KuCoinMarket> KuCoinMarkets { get; set; }
        

        // Asset prices
        public DbSet<GeneralAssetPrice> GeneralAssetPrices { get; set; }
        public DbSet<KLineAssetPrice> KLineAssetPrices { get; set; }

        public DbSet<BitgetAssetPrice> BitgetAssetPrices { get; set; }
        public DbSet<BinanceAssetPrice> BinanceAssetPrices { get; set; }
        public DbSet<BybitAssetPrice> BybitAssetPrices { get; set; }
        public DbSet<OkxAssetPrice> OkxAssetPrices { get; set; }
        public DbSet<KuCoinAssetPrice> KuCoinAssetPrices { get; set; }

        // Removed Asset
        public DbSet<BitgetRemovedAsset> BitgetRemovedAssets { get; set; }
		public DbSet<BinanceRemovedAsset> BinanceRemovedAssets { get; set; }
        public DbSet<BybitRemovedAsset> BybitRemovedAssets { get; set; }
        public DbSet<OkxRemovedAsset> OkxRemovedAssets { get; set; }
        public DbSet<KuCoinRemovedAsset> KuCoinRemovedAssets { get; set; }

        // User Data
        public DbSet<UserData> UsersData { get; set; }
        public DbSet<ProviderSettings> ProvidersSettings { get; set; }
        public DbSet<UserNotificationSettings> UserNotificationSettings { get; set; }

        // Exchanges
        public DbSet<Exchange> Exchanges { get; set; }

        // Error logging
        public DbSet<ErrorLog> ErrorLogs { get; set; }

        // Analytics
        public DbSet<Analytics> Analytics { get; set; }

        // Parsing Rules
        public DbSet<SignalProvider> SignalProviders { get; set; }
        public DbSet<ProviderParsingRule> ProviderParsingRules { get; set; }

        // Portfolios
        public DbSet<Portfolio> Portfolios { get; set; }
        public DbSet<PortfolioHolding> PortfolioHoldings { get; set; }

        // Admin feature flags
        public DbSet<AdminSetting> AdminSettings { get; set; }


        // OnModelCreating method to configure unique indexes
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<BitgetMarket>()
                .HasIndex(b => new { b.Symbol, b.Type })
                .IsUnique();

            modelBuilder.Entity<GeneralAssetPrice>()
                .HasIndex(b => new { b.Symbol, b.Type })
                .IsUnique();

            modelBuilder.Entity<BitgetAssetPrice>()
                .HasIndex(b => new { b.Symbol, b.Type })
                .IsUnique();

            modelBuilder.Entity<BinanceAssetPrice>()
                .HasIndex(b => new { b.Symbol, b.Type })
                .IsUnique();

            modelBuilder.Entity<BybitAssetPrice>()
                .HasIndex(b => new { b.Symbol, b.Type })
                .IsUnique();

            modelBuilder.Entity<OkxAssetPrice>()
                .HasIndex(b => new { b.Symbol, b.Type })
                .IsUnique();

            modelBuilder.Entity<KuCoinAssetPrice>()
                .HasIndex(b => new { b.Symbol, b.Type })
                .IsUnique();

            modelBuilder.Entity<SignalPrediction>()
                .HasIndex(prediction => prediction.SignalId)
                .IsUnique();

            // --- Performance indexes (C1) ---

            // Orders
            modelBuilder.Entity<Order>()
                .HasIndex(o => o.Status);
            modelBuilder.Entity<Order>()
                .HasIndex(o => o.UserId);
            modelBuilder.Entity<Order>()
                .HasIndex(o => new { o.UserId, o.SignalId, o.Symbol });

            // Positions
            modelBuilder.Entity<Position>()
                .HasIndex(p => p.Status);
            modelBuilder.Entity<Position>()
                .HasIndex(p => p.UserId);
            modelBuilder.Entity<Position>()
                .HasIndex(p => new { p.UserId, p.Symbol, p.Side, p.Status });

            // Analytics — hit on every page load
            modelBuilder.Entity<Analytics>()
                .HasIndex(a => new { a.PageName, a.Date });

            // SignalPerformances — queried by Status every 3 minutes
            modelBuilder.Entity<SignalPerformance>()
                .HasIndex(sp => sp.Status);

            // SignalPerformances — joined to Signals by SignalId
            modelBuilder.Entity<SignalPerformance>()
                .HasIndex(sp => sp.SignalId);

            // Signals — queried by Symbol and looked up by Id throughout the app
            modelBuilder.Entity<Signal>()
                .HasIndex(s => s.Symbol);
            modelBuilder.Entity<Signal>()
                .HasIndex(s => s.Provider);

            // UsersData — queried for SubscriptionActive on every signal
            modelBuilder.Entity<UserData>()
                .HasIndex(u => u.SubscriptionActive);

            // ErrorLogs — displayed in descending order
            modelBuilder.Entity<ErrorLog>()
                .HasIndex(e => e.Timestamp);

            // GeneralAssetPrices — queried by Symbol in watchdog fallback
            modelBuilder.Entity<GeneralAssetPrice>()
                .HasIndex(g => g.Symbol);

            // GeneralAssetPrices — queried by Symbol + Time in TrackPerformance
            modelBuilder.Entity<GeneralAssetPrice>()
                .HasIndex(g => new { g.Symbol, g.Time });

            // KLineAssetPrices — primary query: WHERE Symbol=? AND Type=? AND Time>=?
            // Covering index includes every column CandleService selects, eliminating key lookups.
            modelBuilder.Entity<KLineAssetPrice>()
                .HasIndex(k => new { k.Symbol, k.Type, k.Time })
                .IncludeProperties(k => new { k.Price, k.Open, k.High, k.Low, k.Close, k.Volume });

            // UserNotificationSettings — one row per user
            modelBuilder.Entity<UserNotificationSettings>()
                .HasIndex(n => n.UserId)
                .IsUnique();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseSqlServer("Server=localhost;Database=AutoSignals;Integrated Security=SSPI;MultipleActiveResultSets=true;Encrypt=false");
        }
        public DbSet<AutoSignals.Models.Provider> Provider { get; set; } = default!;
        public DbSet<AutoSignals.Models.UserFeedback> UserFeedback { get; set; } = default!;
        public DbSet<UserFeedbackImage> UserFeedbackImages { get; set; }

    }
}