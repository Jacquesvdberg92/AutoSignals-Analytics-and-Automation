using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using AutoSignals.Data;
using AutoSignals.Models;

public class RoleInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public RoleInitializer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            // Legacy roles — kept for backward compatibility; do not remove.
            // "Tester" users are treated as VIP equivalent (see RequiresVIP policy in Program.cs).
            // New subscription roles: "Freemium" (default), "Pro" (paid/trial), "VIP" (paid).
            string[] roleNames = { "Free User", "Tester", "Subscriber", "VIP", "Admin", "Freemium", "Pro" };
            foreach (var roleName in roleNames)
            {
                var roleExists = await roleManager.RoleExistsAsync(roleName);
                if (!roleExists)
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            await InsertExchanges(context);
        }
    }

    private async Task InsertExchanges(AutoSignalsDbContext context)
    {
        var exchanges = new List<Exchange>
        {
            new Exchange { Name = "Bitget", Referal = "", Url = "https://www.bitget.com/", ReferalClicked = 0, IsEnabled = false, LogoUrl = "https://img.freepik.com/free-vector/coming-soon-background-with-focus-light-effect-design_1017-27277.jpg?semt=ais_hybrid&w=740&q=80", Description = "Coming soon", ReferralBonus = "Coming Soon", Type = "CEX" },
            new Exchange { Name = "OKX", Referal = "", Url = "https://www.okx.com/", ReferalClicked = 0, IsEnabled = false, LogoUrl = "https://img.freepik.com/free-vector/coming-soon-background-with-focus-light-effect-design_1017-27277.jpg?semt=ais_hybrid&w=740&q=80", Description = "Coming soon", ReferralBonus = "Coming Soon", Type = "CEX" },
            new Exchange { Name = "Binance", Referal = "", Url = "https://www.binance.com/", ReferalClicked = 0, IsEnabled = false, LogoUrl = "https://img.freepik.com/free-vector/coming-soon-background-with-focus-light-effect-design_1017-27277.jpg?semt=ais_hybrid&w=740&q=80", Description = "Coming soon", ReferralBonus = "Coming Soon", Type = "CEX" },
            new Exchange { Name = "Bybit", Referal = "", Url = "https://www.bybit.com/", ReferalClicked = 0, IsEnabled = false, LogoUrl = "https://img.freepik.com/free-vector/coming-soon-background-with-focus-light-effect-design_1017-27277.jpg?semt=ais_hybrid&w=740&q=80", Description = "Coming soon", ReferralBonus = "Coming Soon", Type = "CEX" },
            new Exchange { Name = "KuCoin", Referal = "", Url = "https://www.kucoin.com/", ReferalClicked = 0, IsEnabled = false, LogoUrl = "https://img.freepik.com/free-vector/coming-soon-background-with-focus-light-effect-design_1017-27277.jpg?semt=ais_hybrid&w=740&q=80", Description = "Coming soon", ReferralBonus = "Coming Soon", Type = "CEX" }
        };

        foreach (var exchange in exchanges)
        {
            if (!context.Exchanges.Any(e => e.Name == exchange.Name))
            {
                context.Exchanges.Add(exchange);
            }
        }

        await context.SaveChangesAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
