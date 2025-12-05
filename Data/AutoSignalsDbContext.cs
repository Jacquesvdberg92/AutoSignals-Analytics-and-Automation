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

        // Exchanges
        public DbSet<Exchange> Exchanges { get; set; }

        // Error logging
        public DbSet<ErrorLog> ErrorLogs { get; set; }

        // Analytics
        public DbSet<Analytics> Analytics { get; set; }

        // OnModelCreating method to configure unique indexes
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<GeneralAssetPrice>()
                .HasIndex(b => b.Symbol)
                .IsUnique();

            modelBuilder.Entity<BitgetAssetPrice>()
                .HasIndex(b => b.Symbol)
                .IsUnique();

            modelBuilder.Entity<BinanceAssetPrice>()
                .HasIndex(b => b.Symbol)
                .IsUnique();

            modelBuilder.Entity<BybitAssetPrice>()
                .HasIndex(b => b.Symbol)
                .IsUnique();

            modelBuilder.Entity<OkxAssetPrice>()
                .HasIndex(b => b.Symbol)
                .IsUnique();

            modelBuilder.Entity<KuCoinAssetPrice>()
                .HasIndex(b => b.Symbol)
                .IsUnique();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Server=localhost;Database=AutoSignals;Integrated Security=SSPI;MultipleActiveResultSets=true;Encrypt=false");
        public DbSet<AutoSignals.Models.Provider> Provider { get; set; } = default!;
        public DbSet<AutoSignals.Models.UserFeedback> UserFeedback { get; set; } = default!;
        public DbSet<UserFeedbackImage> UserFeedbackImages { get; set; }

    }
}