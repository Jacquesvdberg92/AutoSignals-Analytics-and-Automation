using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
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

// Set the culture to invariant (uses '.' as the decimal separator)
var cultureInfo = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

// Configure maximum file size for uploads
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104857600; // 100 MB
});

// Add services to the container.
builder.Services.AddControllersWithViews();

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


// Register the TelegramBotClient
var botToken = builder.Configuration["TelegramBot:Token"];
builder.Services.AddSingleton<ITelegramBotClient>(provider => new TelegramBotClient(botToken));

// Register TelegramBotService as a singleton
builder.Services.AddSingleton<TelegramBotService>();

// Register the singleton as a hosted service
builder.Services.AddHostedService(provider => provider.GetRequiredService<TelegramBotService>());

// TelegramGroupsOptions configuration
builder.Services.Configure<TelegramGroupsOptions>(
    builder.Configuration.GetSection("TelegramGroups"));

// Retrieve Bitget API credentials from configuration
var bitgetApiKey = builder.Configuration["Bitget:ApiKey"] ?? throw new InvalidOperationException("Bitget API key not found.");
var bitgetApiSecret = builder.Configuration["Bitget:ApiSecret"] ?? throw new InvalidOperationException("Bitget API secret not found.");
var bitgetPassword = builder.Configuration["Bitget:Password"] ?? throw new InvalidOperationException("Bitget password not found.");

builder.Services.AddScoped<IBitgetService, BitgetPriceService>(sp =>
{
    var context = sp.GetRequiredService<AutoSignalsDbContext>();
    var errorLogService = sp.GetRequiredService<ErrorLogService>();
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    return new BitgetPriceService(bitgetApiKey, bitgetApiSecret, bitgetPassword, errorLogService, scopeFactory);
});

// Retrieve Binance API credentials from configuration
var binanceApiKey = builder.Configuration["Binance:ApiKey"] ?? throw new InvalidOperationException("Binance API key not found.");
var binanceApiSecret = builder.Configuration["Binance:ApiSecret"] ?? throw new InvalidOperationException("Binance API secret not found.");

// Register BinanceService
builder.Services.AddScoped<IBinanceService, BinancePriceService>(sp =>
{
    var context = sp.GetRequiredService<AutoSignalsDbContext>();
    return new BinancePriceService(binanceApiKey, binanceApiSecret, context);
});

// Retrieve Bybit API credentials from configuration
var bybitApiKey = builder.Configuration["Bybit:ApiKey"] ?? throw new InvalidOperationException("Bybit API key not found.");
var bybitApiSecret = builder.Configuration["Bybit:ApiSecret"] ?? throw new InvalidOperationException("Bybit API secret not found.");

// Register BybitService
builder.Services.AddScoped<IBybitService, BybitPriceService>(sp =>
{
    var context = sp.GetRequiredService<AutoSignalsDbContext>();
    return new BybitPriceService(bybitApiKey, bybitApiSecret, context);
});

// Retrieve OKX API credentials from configuration
var okxApiKey = builder.Configuration["Okx:ApiKey"] ?? throw new InvalidOperationException("OKX API key not found.");
var okxApiSecret = builder.Configuration["Okx:ApiSecret"] ?? throw new InvalidOperationException("OKX API secret not found.");
var okxPassword = builder.Configuration["Okx:Password"] ?? throw new InvalidOperationException("Okx password not found.");

// Register OKXService
builder.Services.AddScoped<IOkxService, OkxPriceService>(sp =>
{
    var context = sp.GetRequiredService<AutoSignalsDbContext>();
    var errorLogService = sp.GetRequiredService<ErrorLogService>();
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    return new OkxPriceService(okxApiKey, okxApiSecret, okxPassword, errorLogService, scopeFactory);
});

// Retrieve KuCoin API credentials from configuration
var kucoinApiKey = builder.Configuration["KuCoin:ApiKey"] ?? throw new InvalidOperationException("KuCoin API key not found.");
var kucoinApiSecret = builder.Configuration["KuCoin:ApiSecret"] ?? throw new InvalidOperationException("KuCoin API secret not found.");
var kucoinPassword = builder.Configuration["KuCoin:Password"] ?? throw new InvalidOperationException("KuCoin password not found.");

// Register BybitService
builder.Services.AddScoped<IKuCoinService, KuCoinPriceService>(sp =>
{
    var context = sp.GetRequiredService<AutoSignalsDbContext>();
    return new KuCoinPriceService(kucoinApiKey, kucoinApiSecret, kucoinPassword, context);
});

// Register AveragePriceService
builder.Services.AddScoped<AveragePriceService>();

// Register SignalPerformanceService
builder.Services.AddScoped<SignalPerformanceService>(sp =>
{
    var context = sp.GetRequiredService<AutoSignalsDbContext>();
    var telegramBotService = sp.GetRequiredService<TelegramBotService>();
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new SignalPerformanceService(context, telegramBotService, scopeFactory, env);
});

// Register UserOrderWatchDogService - This service will monitor user orders and execute them when the conditions are met
builder.Services.AddSingleton<UserOrderWatchDogService>();

// Register ExchangeHostedService as a hosted service
builder.Services.AddHostedService<ExchangeHostedService>();

// Register SignalProviderService
builder.Services.AddScoped<SignalProviderService>();

// Add EmailSender service
builder.Services.AddSingleton<IEmailSender, EmailSender>();
builder.Services.AddTransient<MailerController>();

// Register RoleInitializer
builder.Services.AddHostedService<RoleInitializer>();

// Error logging service
builder.Services.AddScoped<ErrorLogService>();

// Register OrderService
builder.Services.AddScoped<OrderService>();

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

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages(); // Map Razor Pages

// Add global exception handling
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

app.Run();
