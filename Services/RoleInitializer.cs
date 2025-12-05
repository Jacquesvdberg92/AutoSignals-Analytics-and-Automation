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

            string[] roleNames = { "Free User", "Tester", "Subscriber", "VIP", "Admin" };
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
            new Exchange { Name = "Bitget", Referal = "", Url = "https://www.bitget.com/", ReferalClicked = 0, IsEnabled = false },
            new Exchange { Name = "OKX", Referal = "", Url = "https://www.okx.com/", ReferalClicked = 0, IsEnabled = false },
            new Exchange { Name = "Binance", Referal = "", Url = "https://www.binance.com/", ReferalClicked = 0, IsEnabled = false },
            new Exchange { Name = "Bybit", Referal = "", Url = "https://www.bybit.com/", ReferalClicked = 0, IsEnabled = false },
            new Exchange { Name = "KuCoin", Referal = "", Url = "https://www.kucoin.com/", ReferalClicked = 0, IsEnabled = false }
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
