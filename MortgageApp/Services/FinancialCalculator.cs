using MortgageApp.Data;

namespace MortgageApp.Services;

public static class FinancialCalculator
{
    public static decimal CalculateCashFlowForMonth(
        IEnumerable<CashFlowEntry> entries,
        DateTime month,
        CashFlowType type)
        => entries
            .Where(entry => entry.Type == type && OccursInMonth(entry, month))
            .Sum(entry => entry.MonthlyAmount);

    public static IReadOnlyList<CashFlowProjectionRow> ProjectCashFlow(
        IEnumerable<CashFlowEntry> entries,
        DateTime startMonth,
        MortgagePlan? mortgagePlan = null,
        decimal savingsRatePercent = 100m,
        decimal investmentRatePercent = 0m,
        int months = 12)
    {
        var normalizedStart = new DateTime(startMonth.Year, startMonth.Month, 1);
        var normalizedSavingsRate = Math.Clamp(savingsRatePercent, 0m, 100m);
        var normalizedInvestmentRate = Math.Clamp(investmentRatePercent, 0m, 100m - normalizedSavingsRate);
        decimal cumulativeFreeAmount = 0;
        return Enumerable.Range(0, months)
            .Select(offset =>
            {
                var month = normalizedStart.AddMonths(offset);
                var income = CalculateCashFlowForMonth(entries, month, CashFlowType.Income);
                var mortgagePayment = CalculateMortgagePaymentForMonth(mortgagePlan, month);
                var expenses = CalculateCashFlowForMonth(entries, month, CashFlowType.Expense) + mortgagePayment;
                var freeBeforeSavings = income - expenses;
                var savingsContribution = Math.Max(0, freeBeforeSavings) *
                                          normalizedSavingsRate / 100m;
                var investmentContribution = Math.Max(0, freeBeforeSavings) *
                                             normalizedInvestmentRate / 100m;
                var freeAmount = freeBeforeSavings - savingsContribution - investmentContribution;
                cumulativeFreeAmount += freeAmount;
                return new CashFlowProjectionRow(
                    month,
                    income,
                    expenses,
                    savingsContribution,
                    investmentContribution,
                    freeBeforeSavings,
                    freeAmount,
                    cumulativeFreeAmount,
                    mortgagePayment);
            })
            .ToList();
    }

    public static decimal EstimateFreeCashByDate(
        IEnumerable<CashFlowEntry> entries,
        DateTime startMonth,
        DateTime targetDate,
        MortgagePlan? mortgagePlan,
        decimal savingsRatePercent,
        decimal investmentRatePercent = 0m)
    {
        var normalizedStart = new DateTime(startMonth.Year, startMonth.Month, 1);
        var normalizedTarget = new DateTime(targetDate.Year, targetDate.Month, 1);
        var months = (normalizedTarget.Year - normalizedStart.Year) * 12 +
                     normalizedTarget.Month - normalizedStart.Month + 1;

        if (months <= 0)
        {
            return 0;
        }

        return ProjectCashFlow(
                entries,
                normalizedStart,
                mortgagePlan,
                savingsRatePercent,
                investmentRatePercent,
                months)[^1]
            .CumulativeFreeAmount;
    }

    public static FinancialPositionForecast ForecastFinancialPosition(
        IEnumerable<CashFlowEntry> entries,
        IReadOnlyCollection<SavingsAccount> savingsAccounts,
        DateTime startMonth,
        DateTime targetDate,
        MortgagePlan? mortgagePlan,
        decimal savingsRatePercent,
        decimal investmentRatePercent = 0m,
        decimal currentInvestmentBalance = 0m)
    {
        var normalizedStart = new DateTime(startMonth.Year, startMonth.Month, 1);
        var normalizedTarget = new DateTime(targetDate.Year, targetDate.Month, 1);
        var months = (normalizedTarget.Year - normalizedStart.Year) * 12 +
                     normalizedTarget.Month - normalizedStart.Month + 1;
        var currentSavings = savingsAccounts.Sum(x => x.Balance);

        if (months <= 0)
        {
            return new FinancialPositionForecast(
                normalizedTarget,
                currentSavings,
                currentInvestmentBalance,
                0,
                currentSavings + currentInvestmentBalance,
                0,
                0,
                0);
        }

        var projection = ProjectFinancialPosition(
            entries,
            savingsAccounts,
            normalizedStart,
            mortgagePlan,
            savingsRatePercent,
            investmentRatePercent,
            currentInvestmentBalance,
            months);
        var last = projection[^1];
        return new FinancialPositionForecast(
            normalizedTarget,
            last.SavingsBalance,
            last.InvestmentBalance,
            last.CashOutsideSavings,
            last.TotalAvailable,
            last.CumulativeSavingsDeposits,
            last.CumulativeInvestmentDeposits,
            last.CumulativeNetInterest);
    }

    public static CurrentSavingsPosition CalculateCurrentSavingsPosition(
        IEnumerable<CashFlowEntry> entries,
        IReadOnlyCollection<SavingsAccount> savingsAccounts,
        DateTime asOfDate,
        MortgagePlan? mortgagePlan,
        decimal savingsRatePercent,
        decimal investmentRatePercent = 0m)
    {
        var confirmedBalance = savingsAccounts.Sum(x => x.Balance);
        var balances = savingsAccounts.ToDictionary(x => x.Id, x => x.Balance);
        if (savingsAccounts.Count == 0)
        {
            return new CurrentSavingsPosition(asOfDate, confirmedBalance, 0, 0, confirmedBalance, balances);
        }

        var confirmedAt = savingsAccounts.Max(x => x.BalanceUpdatedAtUtc ?? x.UpdatedAtUtc);
        var (startMonth, months) = GetProjectionWindowAfterConfirmation(confirmedAt, asOfDate);
        if (months == 0)
        {
            return new CurrentSavingsPosition(confirmedAt, confirmedBalance, 0, 0, confirmedBalance, balances);
        }

        var cashFlow = ProjectCashFlow(
            entries,
            startMonth,
            mortgagePlan,
            savingsRatePercent,
            investmentRatePercent,
            months);
        var projection = ProjectSavingsPortfolio(savingsAccounts, cashFlow.Select(x => x.SavingsContribution));
        var last = projection[^1];
        var plannedDeposits = projection.Sum(row => row.Allocations.Sum(x => x.Amount));
        var calculatedInterest = last.TotalBalance - confirmedBalance - plannedDeposits;

        return new CurrentSavingsPosition(
            confirmedAt,
            confirmedBalance,
            plannedDeposits,
            calculatedInterest,
            last.TotalBalance,
            last.AccountBalances);
    }

    public static CurrentInvestmentPosition CalculateCurrentInvestmentPosition(
        IEnumerable<CashFlowEntry> entries,
        decimal confirmedBalance,
        DateTime? confirmedAt,
        DateTime asOfDate,
        MortgagePlan? mortgagePlan,
        decimal savingsRatePercent,
        decimal investmentRatePercent)
    {
        if (confirmedAt is null)
        {
            return new CurrentInvestmentPosition(null, confirmedBalance, 0, confirmedBalance);
        }

        var (startMonth, months) = GetProjectionWindowAfterConfirmation(confirmedAt.Value, asOfDate);
        if (months == 0)
        {
            return new CurrentInvestmentPosition(confirmedAt, confirmedBalance, 0, confirmedBalance);
        }

        var calculatedDeposits = ProjectCashFlow(
                entries,
                startMonth,
                mortgagePlan,
                savingsRatePercent,
                investmentRatePercent,
                months)
            .Sum(x => x.InvestmentContribution);
        return new CurrentInvestmentPosition(
            confirmedAt,
            confirmedBalance,
            calculatedDeposits,
            confirmedBalance + calculatedDeposits);
    }

    public static IReadOnlyList<FinancialPositionProjectionRow> ProjectFinancialPosition(
        IEnumerable<CashFlowEntry> entries,
        IReadOnlyCollection<SavingsAccount> savingsAccounts,
        DateTime startMonth,
        MortgagePlan? mortgagePlan,
        decimal savingsRatePercent,
        decimal investmentRatePercent = 0m,
        decimal currentInvestmentBalance = 0m,
        int months = 12)
    {
        var currentSavings = savingsAccounts.Sum(x => x.Balance);
        var cashFlow = ProjectCashFlow(
            entries,
            startMonth,
            mortgagePlan,
            savingsRatePercent,
            investmentRatePercent,
            months);
        var savingsProjection = ProjectSavingsPortfolio(
            savingsAccounts,
            cashFlow.Select(x => x.SavingsContribution));
        var rows = new List<FinancialPositionProjectionRow>(months);
        decimal cumulativeDeposits = 0;
        decimal cumulativeInvestmentDeposits = 0;
        decimal cumulativeUnallocated = 0;

        for (var index = 0; index < cashFlow.Count; index++)
        {
            var savingsRow = savingsProjection[index];
            cumulativeDeposits += savingsRow.Allocations.Sum(x => x.Amount);
            cumulativeInvestmentDeposits += cashFlow[index].InvestmentContribution;
            cumulativeUnallocated += savingsRow.UnallocatedAmount;
            var cashOutsideSavings = cashFlow[index].CumulativeFreeAmount + cumulativeUnallocated;
            var cumulativeInterest = savingsRow.TotalBalance - currentSavings - cumulativeDeposits;

            rows.Add(new FinancialPositionProjectionRow(
                cashFlow[index].Month,
                savingsRow.TotalBalance,
                currentInvestmentBalance + cumulativeInvestmentDeposits,
                cashOutsideSavings,
                savingsRow.TotalBalance + currentInvestmentBalance + cumulativeInvestmentDeposits + cashOutsideSavings,
                cumulativeDeposits,
                cumulativeInvestmentDeposits,
                savingsRow.NetInterest,
                cumulativeInterest));
        }

        return rows;
    }

    public static decimal CalculateMortgagePaymentForMonth(MortgagePlan? plan, DateTime month)
    {
        if (plan is null)
        {
            return 0;
        }

        var firstPaymentMonth = new DateTime(plan.FirstPaymentDate.Year, plan.FirstPaymentDate.Month, 1);
        var targetMonth = new DateTime(month.Year, month.Month, 1);
        var monthIndex = (targetMonth.Year - firstPaymentMonth.Year) * 12 +
                         targetMonth.Month - firstPaymentMonth.Month;
        var paymentCount = plan.TermYears * 12;

        if (monthIndex < 0 || monthIndex >= paymentCount)
        {
            return 0;
        }

        return CalculateMortgage(plan).MonthlyPayment;
    }

    public static bool OccursInMonth(CashFlowEntry entry, DateTime month)
    {
        var occurrence = new DateTime(
            entry.FirstOccurrenceMonth.Year,
            entry.FirstOccurrenceMonth.Month,
            1);
        var target = new DateTime(month.Year, month.Month, 1);
        var difference = (target.Year - occurrence.Year) * 12 + target.Month - occurrence.Month;
        return entry.IntervalMonths is null
            ? difference == 0
            : difference >= 0 && difference % entry.IntervalMonths.Value == 0;
    }

    public static decimal CalculateAnnualGrossInterest(
        decimal balance,
        IEnumerable<SavingsRateTier> tiers,
        bool useBonusRates)
    {
        if (balance <= 0)
        {
            return 0;
        }

        decimal total = 0;
        foreach (var tier in tiers.OrderBy(x => x.FromAmount))
        {
            var tierEnd = tier.ToAmount ?? balance;
            var amountInTier = Math.Max(0, Math.Min(balance, tierEnd) - tier.FromAmount);
            total += amountInTier * GetGrossRate(tier, useBonusRates) / 100m;
        }

        return total;
    }

    public static decimal GetGrossRate(SavingsRateTier tier, bool useBonusRate)
        => tier.BaseAnnualRate + (useBonusRate ? tier.BonusAnnualRate : 0m);

    public static SavingsAllocationPlan PlanSavingsAllocation(
        IReadOnlyCollection<SavingsAccount> accounts,
        decimal amount)
    {
        var balances = accounts.ToDictionary(x => x.Id, x => x.Balance);
        return PlanSavingsAllocation(accounts, amount, balances);
    }

    public static IReadOnlyList<SavingsPortfolioProjectionRow> ProjectSavingsPortfolio(
        IReadOnlyCollection<SavingsAccount> accounts,
        decimal monthlyContribution,
        int months = 12)
        => ProjectSavingsPortfolio(accounts, Enumerable.Repeat(monthlyContribution, months));

    public static IReadOnlyList<SavingsPortfolioProjectionRow> ProjectSavingsPortfolio(
        IReadOnlyCollection<SavingsAccount> accounts,
        IEnumerable<decimal> monthlyContributions)
    {
        var contributions = monthlyContributions.ToList();
        var rows = new List<SavingsPortfolioProjectionRow>(contributions.Count);
        var balances = accounts.ToDictionary(x => x.Id, x => x.Balance);

        for (var month = 1; month <= contributions.Count; month++)
        {
            decimal netInterest = 0;

            foreach (var account in accounts)
            {
                var grossInterest = CalculateAnnualGrossInterest(
                    balances[account.Id],
                    account.RateTiers,
                    account.MeetsBonusConditions) / 12m;
                var accountNetInterest = grossInterest * (1m - account.InterestTaxPercent / 100m);
                balances[account.Id] += accountNetInterest;
                netInterest += accountNetInterest;
            }

            // A planned monthly contribution is treated as arriving at the end of the month.
            var allocation = PlanSavingsAllocation(accounts, Math.Max(0, contributions[month - 1]), balances);

            rows.Add(new SavingsPortfolioProjectionRow(
                month,
                balances.Values.Sum(),
                netInterest,
                allocation.Allocations,
                allocation.UnallocatedAmount,
                new Dictionary<int, decimal>(balances)));
        }

        return rows;
    }

    public static IReadOnlyList<SavingsProjectionRow> ProjectSavings(
        SavingsAccount account,
        int months = 12)
    {
        var rows = new List<SavingsProjectionRow>(months);
        var balance = account.Balance;

        for (var month = 1; month <= months; month++)
        {
            var grossInterest = CalculateAnnualGrossInterest(
                balance,
                account.RateTiers,
                account.MeetsBonusConditions) / 12m;
            var netInterest = grossInterest * (1m - account.InterestTaxPercent / 100m);
            balance += netInterest;
            rows.Add(new SavingsProjectionRow(month, balance, netInterest));
        }

        return rows;
    }

    public static MortgageResult CalculateMortgage(MortgagePlan plan)
    {
        var principal = Math.Max(0, plan.PropertyPrice - plan.OwnFunds);
        var paymentCount = plan.TermYears * 12;

        if (principal == 0 || paymentCount == 0)
        {
            return new MortgageResult(principal, 0, 0, 0);
        }

        var monthlyRate = plan.AnnualInterestRate / 100m / 12m;
        decimal monthlyPayment;

        if (monthlyRate == 0)
        {
            monthlyPayment = principal / paymentCount;
        }
        else
        {
            var factor = (decimal)Math.Pow((double)(1m + monthlyRate), paymentCount);
            monthlyPayment = principal * monthlyRate * factor / (factor - 1m);
        }

        var totalPaid = monthlyPayment * paymentCount;
        var ltv = plan.PropertyPrice == 0 ? 0 : principal / plan.PropertyPrice * 100m;
        return new MortgageResult(principal, monthlyPayment, totalPaid, ltv);
    }

    private static SavingsAllocationPlan PlanSavingsAllocation(
        IReadOnlyCollection<SavingsAccount> accounts,
        decimal amount,
        Dictionary<int, decimal> balances)
    {
        var remaining = Math.Max(0, amount);
        var allocations = new List<SavingsAllocation>();

        while (remaining > 0)
        {
            var best = accounts
                .Select(account => GetMarginalOption(account, balances[account.Id], remaining))
                .Where(option => option is not null)
                .Cast<MarginalSavingsOption>()
                .OrderByDescending(option => option.NetAnnualRate)
                .ThenBy(option => option.AccountName)
                .FirstOrDefault();

            if (best is null)
            {
                break;
            }

            var allocatedAmount = Math.Min(remaining, best.Capacity);
            balances[best.AccountId] += allocatedAmount;
            remaining -= allocatedAmount;

            var last = allocations.LastOrDefault();
            if (last is not null &&
                last.AccountId == best.AccountId &&
                last.NetAnnualRate == best.NetAnnualRate)
            {
                allocations[^1] = last with { Amount = last.Amount + allocatedAmount };
            }
            else
            {
                allocations.Add(new SavingsAllocation(
                    best.AccountId,
                    best.AccountName,
                    best.BankName,
                    allocatedAmount,
                    best.GrossAnnualRate,
                    best.NetAnnualRate));
            }
        }

        return new SavingsAllocationPlan(allocations, remaining);
    }

    private static MarginalSavingsOption? GetMarginalOption(
        SavingsAccount account,
        decimal balance,
        decimal remaining)
    {
        var tiers = account.RateTiers.OrderBy(x => x.FromAmount).ToList();
        if (tiers.Count == 0)
        {
            return null;
        }

        var tier = tiers.FirstOrDefault(x =>
            balance >= x.FromAmount &&
            (x.ToAmount is null || balance < x.ToAmount));

        if (tier is null)
        {
            tier = tiers.FirstOrDefault(x => x.FromAmount > balance);
        }

        if (tier is null)
        {
            return null;
        }

        var capacity = tier.ToAmount is null
            ? remaining
            : Math.Max(0, tier.ToAmount.Value - balance);

        if (capacity <= 0)
        {
            return null;
        }

        var grossRate = GetGrossRate(tier, account.MeetsBonusConditions);
        var netRate = grossRate * (1m - account.InterestTaxPercent / 100m);
        return new MarginalSavingsOption(
            account.Id,
            account.Name,
            account.BankName,
            capacity,
            grossRate,
            netRate);
    }

    private static (DateTime StartMonth, int Months) GetProjectionWindowAfterConfirmation(
        DateTime confirmedAt,
        DateTime asOfDate)
    {
        var startMonth = new DateTime(confirmedAt.Year, confirmedAt.Month, 1).AddMonths(1);
        var targetMonth = new DateTime(asOfDate.Year, asOfDate.Month, 1);
        var months = (targetMonth.Year - startMonth.Year) * 12 + targetMonth.Month - startMonth.Month + 1;
        return (startMonth, Math.Max(0, months));
    }

    private sealed record MarginalSavingsOption(
        int AccountId,
        string AccountName,
        string BankName,
        decimal Capacity,
        decimal GrossAnnualRate,
        decimal NetAnnualRate);
}

public record SavingsAllocation(
    int AccountId,
    string AccountName,
    string BankName,
    decimal Amount,
    decimal GrossAnnualRate,
    decimal NetAnnualRate);

public record SavingsAllocationPlan(
    IReadOnlyList<SavingsAllocation> Allocations,
    decimal UnallocatedAmount);

public record SavingsPortfolioProjectionRow(
    int Month,
    decimal TotalBalance,
    decimal NetInterest,
    IReadOnlyList<SavingsAllocation> Allocations,
    decimal UnallocatedAmount,
    IReadOnlyDictionary<int, decimal> AccountBalances);

public record CurrentSavingsPosition(
    DateTime ConfirmedAt,
    decimal ConfirmedBalance,
    decimal CalculatedDeposits,
    decimal CalculatedInterest,
    decimal EstimatedCurrentBalance,
    IReadOnlyDictionary<int, decimal> AccountBalances);

public record CurrentInvestmentPosition(
    DateTime? ConfirmedAt,
    decimal ConfirmedBalance,
    decimal CalculatedDeposits,
    decimal EstimatedCurrentBalance);

public record SavingsProjectionRow(int Month, decimal Balance, decimal NetInterest);

public record CashFlowProjectionRow(
    DateTime Month,
    decimal Income,
    decimal Expenses,
    decimal SavingsContribution,
    decimal InvestmentContribution,
    decimal FreeBeforeSavings,
    decimal FreeAmount,
    decimal CumulativeFreeAmount,
    decimal MortgagePayment);

public record FinancialPositionForecast(
    DateTime TargetMonth,
    decimal SavingsBalance,
    decimal InvestmentBalance,
    decimal CashOutsideSavings,
    decimal TotalAvailable,
    decimal SavingsDeposits,
    decimal InvestmentDeposits,
    decimal NetInterest);

public record FinancialPositionProjectionRow(
    DateTime Month,
    decimal SavingsBalance,
    decimal InvestmentBalance,
    decimal CashOutsideSavings,
    decimal TotalAvailable,
    decimal CumulativeSavingsDeposits,
    decimal CumulativeInvestmentDeposits,
    decimal NetInterest,
    decimal CumulativeNetInterest);

public record MortgageResult(decimal Principal, decimal MonthlyPayment, decimal TotalPaid, decimal LtvPercent);
