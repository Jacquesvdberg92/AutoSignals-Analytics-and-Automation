using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using AutoSignals.Services.ExchangeAdapters;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // Add Razor Pages
builder.Services.AddMemoryCache();

// Set the culture to invariant (uses '.' as the decimal separator)
var cultureInfo = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

// Configure maximum file size for uploads
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104857600; // 100 MB
});

// Exchange balance service
builder.Services.AddScoped<ExchangeBalanceService>();

// Encryption
builder.Services.AddSingleton<AesEncryptionService>();

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Add DbContexts
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDbContext<AutoSignalsDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add Identity services
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Google captcha
builder.Services.AddHttpClient();
builder.Services.AddScoped<RecaptchaService>();


var botToken = builder.Configuration["TelegramBot:Token"];
var hasTelegramToken = !string.IsNullOrWhiteSpace(botToken);

if (hasTelegramToken)
{
    builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
    builder.Services.AddSingleton<TelegramBotService>();
    builder.Services.AddSingleton<ITelegramNotifier>(provider => provider.GetRequiredService<TelegramBotService>());
}
else
{
    builder.Services.AddSingleton<ITelegramNotifier, DisabledTelegramNotifier>();
}

// Parsing
builder.Services.AddSingleton<DynamicSignalParserService>();
builder.Services.AddSingleton<SignalDeduplicationService>();

// Register the singleton as a hosted service when Telegram is enabled
if (hasTelegramToken)
{
    builder.Services.AddHostedService(provider => provider.GetRequiredService<TelegramBotService>());
}

// TelegramGroupsOptions configuration
builder.Services.Configure<TelegramGroupsOptions>(
    builder.Configuration.GetSection("TelegramGroups"));

// Register exchange integrations without hard-failing startup when optional config is missing.
var bitgetApiKey = builder.Configuration["Bitget:ApiKey"];
var bitgetApiSecret = builder.Configuration["Bitget:ApiSecret"];
var bitgetPassword = builder.Configuration["Bitget:Password"];
var hasBitgetConfig =
    !string.IsNullOrWhiteSpace(bitgetApiKey) &&
    !string.IsNullOrWhiteSpace(bitgetApiSecret) &&
    !string.IsNullOrWhiteSpace(bitgetPassword);

if (hasBitgetConfig)
{
    builder.Services.AddScoped<IBitgetService>(sp =>
    {
        var errorLogService = sp.GetRequiredService<ErrorLogService>();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return new BitgetPriceService(bitgetApiKey!, bitgetApiSecret!, bitgetPassword!, errorLogService, scopeFactory);
    });
}
else
{
    builder.Services.AddScoped<IBitgetService>(sp => new DisabledExchangeService(
        sp.GetRequiredService<ILogger<DisabledExchangeService>>(),
        "Bitget"));
}

var binanceApiKey = builder.Configuration["Binance:ApiKey"];
var binanceApiSecret = builder.Configuration["Binance:ApiSecret"];
var hasBinanceConfig =
    !string.IsNullOrWhiteSpace(binanceApiKey) &&
    !string.IsNullOrWhiteSpace(binanceApiSecret);

if (hasBinanceConfig)
{
    builder.Services.AddScoped<IBinanceService>(sp =>
    {
        var errorLogService = sp.GetRequiredService<ErrorLogService>();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return new BinancePriceService(binanceApiKey!, binanceApiSecret!, errorLogService, scopeFactory);
    });
}
else
{
    builder.Services.AddScoped<IBinanceService>(sp => new DisabledExchangeService(
        sp.GetRequiredService<ILogger<DisabledExchangeService>>(),
        "Binance"));
}

var bybitApiKey = builder.Configuration["Bybit:ApiKey"];
var bybitApiSecret = builder.Configuration["Bybit:ApiSecret"];
var hasBybitConfig =
    !string.IsNullOrWhiteSpace(bybitApiKey) &&
    !string.IsNullOrWhiteSpace(bybitApiSecret);

if (hasBybitConfig)
{
    builder.Services.AddScoped<IBybitService>(sp =>
    {
        var errorLogService = sp.GetRequiredService<ErrorLogService>();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return new BybitPriceService(bybitApiKey!, bybitApiSecret!, errorLogService, scopeFactory);
    });
}
else
{
    builder.Services.AddScoped<IBybitService>(sp => new DisabledExchangeService(
        sp.GetRequiredService<ILogger<DisabledExchangeService>>(),
        "Bybit"));
}

var okxApiKey = builder.Configuration["Okx:ApiKey"];
var okxApiSecret = builder.Configuration["Okx:ApiSecret"];
var okxPassword = builder.Configuration["Okx:Password"];
var hasOkxConfig =
    !string.IsNullOrWhiteSpace(okxApiKey) &&
    !string.IsNullOrWhiteSpace(okxApiSecret) &&
    !string.IsNullOrWhiteSpace(okxPassword);

if (hasOkxConfig)
{
    builder.Services.AddScoped<IOkxService>(sp =>
    {
        var errorLogService = sp.GetRequiredService<ErrorLogService>();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return new OkxPriceService(okxApiKey!, okxApiSecret!, okxPassword!, errorLogService, scopeFactory);
    });
}
else
{
    builder.Services.AddScoped<IOkxService>(sp => new DisabledExchangeService(
        sp.GetRequiredService<ILogger<DisabledExchangeService>>(),
        "OKX"));
}

var kucoinApiKey = builder.Configuration["KuCoin:ApiKey"];
var kucoinApiSecret = builder.Configuration["KuCoin:ApiSecret"];
var kucoinPassword = builder.Configuration["KuCoin:Password"];
var hasKucoinConfig =
    !string.IsNullOrWhiteSpace(kucoinApiKey) &&
    !string.IsNullOrWhiteSpace(kucoinApiSecret) &&
    !string.IsNullOrWhiteSpace(kucoinPassword);

if (hasKucoinConfig)
{
    builder.Services.AddScoped<IKuCoinService>(sp =>
    {
        var errorLogService = sp.GetRequiredService<ErrorLogService>();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return new KuCoinPriceService(kucoinApiKey!, kucoinApiSecret!, kucoinPassword!, errorLogService, scopeFactory);
    });
}
else
{
    builder.Services.AddScoped<IKuCoinService>(sp => new DisabledExchangeService(
        sp.GetRequiredService<ILogger<DisabledExchangeService>>(),
        "KuCoin"));
}

// Register AveragePriceService
builder.Services.AddScoped<AveragePriceService>();

// Register SignalPerformanceService
builder.Services.AddScoped<SignalPerformanceService>(sp =>
{
    var context = sp.GetRequiredService<AutoSignalsDbContext>();
    var telegramNotifier = sp.GetRequiredService<ITelegramNotifier>();
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var errorLogService = sp.GetRequiredService<ErrorLogService>();
    return new SignalPerformanceService(context, telegramNotifier, scopeFactory, env, errorLogService);
});

// Register UserOrderWatchDogService - This service will monitor user orders and execute them when the conditions are met
builder.Services.AddSingleton<UserOrderWatchDogService>();

// Register ExchangeHostedService as a hosted service
builder.Services.AddHostedService<ExchangeHostedService>();

// Register SignalProviderService
builder.Services.AddScoped<SignalProviderService>();
builder.Services.AddScoped<SignalPredictionService>();

// Add EmailSender service
builder.Services.AddSingleton<IEmailSender, EmailSender>();
builder.Services.AddTransient<MailerController>();

// Register RoleInitializer
builder.Services.AddHostedService<RoleInitializer>();

// Error logging service — singleton with background batch-flush (S8)
builder.Services.AddSingleton<ErrorLogService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ErrorLogService>());

builder.Services.AddScoped<IExchangeOrderAdapter, BitgetOrderAdapter>();
builder.Services.AddScoped<IExchangeOrderAdapter, BinanceOrderAdapter>();
builder.Services.AddScoped<IExchangeOrderAdapter, BybitOrderAdapter>();
builder.Services.AddScoped<IExchangeOrderAdapter, OkxOrderAdapter>();
builder.Services.AddScoped<IExchangeOrderAdapter, KuCoinOrderAdapter>();
builder.Services.AddScoped<ExchangeOrderAdapterFactory>();

// Register OrderService
builder.Services.AddScoped<OrderService>();

// Analytics tracking — batches page-view counts in memory, flushes to DB every 60 s
builder.Services.AddSingleton<AnalyticsService>();
builder.Services.AddSingleton<IAnalyticsService>(sp => sp.GetRequiredService<AnalyticsService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<AnalyticsService>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Add global exception handling before endpoint execution so MVC/Razor exceptions are captured.
app.Use(async (context, next) =>
{
    try
    {
        await next.Invoke();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An unhandled exception occurred.");
        throw;
    }
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages(); // Map Razor Pages

app.Run();
