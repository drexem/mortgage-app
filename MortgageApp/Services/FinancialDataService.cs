using Microsoft.EntityFrameworkCore;
using MortgageApp.Data;

namespace MortgageApp.Services;

public class FinancialDataService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<List<SavingsAccount>> GetSavingsAccountsAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SavingsAccounts
            .Include(x => x.RateTiers)
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    public async Task<SavingsAccount> CreateSavingsAccountAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var account = new SavingsAccount
        {
            UserId = userId,
            BalanceUpdatedAtUtc = DateTime.UtcNow,
            RateTiers =
            [
                new SavingsRateTier
                {
                    FromAmount = 0,
                    ToAmount = null,
                    BaseAnnualRate = 0,
                    BonusAnnualRate = 0,
                    SortOrder = 1
                }
            ]
        };
        db.SavingsAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    public async Task<MortgagePlan> GetOrCreateMortgagePlanAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var plan = await db.MortgagePlans.SingleOrDefaultAsync(x => x.UserId == userId);

        if (plan is not null)
        {
            return plan;
        }

        plan = new MortgagePlan { UserId = userId };
        db.MortgagePlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }
}
