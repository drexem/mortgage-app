using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using MortgageApp.Data;
using MortgageApp.Services;

namespace MortgageApp.Tests;

public class FinancialDataServiceTests
{
    [Fact]
    public async Task CreateSavingsAccount_AllowsMultipleAccountsForOneUser()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

        await using (var provider = services.BuildServiceProvider())
        {
            var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
            }

            var service = new FinancialDataService(factory);
            await service.CreateSavingsAccountAsync("user-1");
            await service.CreateSavingsAccountAsync("user-1");

            var accounts = await service.GetSavingsAccountsAsync("user-1");

            Assert.Equal(2, accounts.Count);
            Assert.All(accounts, account =>
            {
                var tier = Assert.Single(account.RateTiers);
                Assert.Equal(0, tier.FromAmount);
                Assert.Null(tier.ToAmount);
                Assert.Equal(0, tier.BaseAnnualRate);
                Assert.Equal(0, tier.BonusAnnualRate);
            });
        }

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }
}
