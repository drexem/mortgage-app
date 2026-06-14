using MortgageApp.Data;
using MortgageApp.Services;

namespace MortgageApp.Tests;

public class FinancialCalculatorTests
{
    [Fact]
    public void CalculateCashFlowForMonth_IncludesOneTimeIncomeOnlyOnce()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 50_000m,
                IntervalMonths = null,
                FirstOccurrenceMonth = new DateTime(2026, 7, 1)
            }
        };

        Assert.Equal(
            50_000m,
            FinancialCalculator.CalculateCashFlowForMonth(entries, new DateTime(2026, 7, 1), CashFlowType.Income));
        Assert.Equal(
            0m,
            FinancialCalculator.CalculateCashFlowForMonth(entries, new DateTime(2026, 8, 1), CashFlowType.Income));
    }

    [Fact]
    public void CalculateCashFlowForMonth_IncludesQuarterlyBonusOnlyInScheduledMonths()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            },
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 30_000m,
                IntervalMonths = 3,
                FirstOccurrenceMonth = new DateTime(2026, 2, 1)
            }
        };

        Assert.Equal(
            130_000m,
            FinancialCalculator.CalculateCashFlowForMonth(entries, new DateTime(2026, 5, 1), CashFlowType.Income));
        Assert.Equal(
            100_000m,
            FinancialCalculator.CalculateCashFlowForMonth(entries, new DateTime(2026, 6, 1), CashFlowType.Income));
    }

    [Fact]
    public void ProjectCashFlow_CalculatesTwelveMonthAverageWithQuarterlyBonus()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            },
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 30_000m,
                IntervalMonths = 3,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };

        var projection = FinancialCalculator.ProjectCashFlow(entries, new DateTime(2026, 1, 1));

        Assert.Equal(12, projection.Count);
        Assert.Equal(110_000m, projection.Average(x => x.Income));
    }

    [Fact]
    public void CalculateAnnualGrossInterest_AppliesEachBalanceTier()
    {
        var tiers = new[]
        {
            new SavingsRateTier { FromAmount = 0, ToAmount = 250_000m, BaseAnnualRate = 3m, BonusAnnualRate = 1m },
            new SavingsRateTier { FromAmount = 250_000m, ToAmount = 1_000_000m, BaseAnnualRate = 1m, BonusAnnualRate = 1m },
            new SavingsRateTier { FromAmount = 1_000_000m, ToAmount = null, BaseAnnualRate = 0.5m, BonusAnnualRate = 0.5m }
        };

        var interest = FinancialCalculator.CalculateAnnualGrossInterest(1_100_000m, tiers, true);

        Assert.Equal(26_000m, interest);
    }

    [Fact]
    public void CalculateAnnualGrossInterest_UsesUserConfiguredBaseRateWhenBonusIsDisabled()
    {
        var tiers = new[]
        {
            new SavingsRateTier
            {
                FromAmount = 0,
                ToAmount = null,
                BaseAnnualRate = 1.25m,
                BonusAnnualRate = 4m
            }
        };

        var interest = FinancialCalculator.CalculateAnnualGrossInterest(200_000m, tiers, false);

        Assert.Equal(2_500m, interest);
    }

    [Fact]
    public void CalculateAnnualGrossInterest_AddsBonusToBaseRate()
    {
        var tiers = new[]
        {
            new SavingsRateTier { FromAmount = 0, ToAmount = null, BaseAnnualRate = 3m, BonusAnnualRate = 1m }
        };

        var interest = FinancialCalculator.CalculateAnnualGrossInterest(100_000m, tiers, true);

        Assert.Equal(4_000m, interest);
    }

    [Fact]
    public void ProjectSavings_AddsNetInterest()
    {
        var account = new SavingsAccount
        {
            Balance = 100_000m,
            InterestTaxPercent = 15m,
            MeetsBonusConditions = true,
            RateTiers =
            [
                new SavingsRateTier { FromAmount = 0, ToAmount = null, BaseAnnualRate = 3m, BonusAnnualRate = 0.6m }
            ]
        };

        var projection = FinancialCalculator.ProjectSavings(account, 1);

        Assert.Single(projection);
        Assert.Equal(100_255m, projection[0].Balance);
        Assert.Equal(255m, projection[0].NetInterest);
    }

    [Fact]
    public void PlanSavingsAllocation_SplitsDepositWhenBestTierIsFilled()
    {
        var accounts = new[]
        {
            new SavingsAccount
            {
                Id = 1,
                Name = "Účet A",
                Balance = 400_000m,
                InterestTaxPercent = 15m,
                RateTiers =
                [
                    new SavingsRateTier { FromAmount = 0, ToAmount = 500_000m, BaseAnnualRate = 4m },
                    new SavingsRateTier { FromAmount = 500_000m, ToAmount = null, BaseAnnualRate = 2m }
                ]
            },
            new SavingsAccount
            {
                Id = 2,
                Name = "Účet B",
                Balance = 0,
                InterestTaxPercent = 15m,
                RateTiers =
                [
                    new SavingsRateTier { FromAmount = 0, ToAmount = null, BaseAnnualRate = 3m }
                ]
            }
        };

        var plan = FinancialCalculator.PlanSavingsAllocation(accounts, 200_000m);

        Assert.Collection(
            plan.Allocations,
            first =>
            {
                Assert.Equal("Účet A", first.AccountName);
                Assert.Equal(100_000m, first.Amount);
                Assert.Equal(3.4m, first.NetAnnualRate);
            },
            second =>
            {
                Assert.Equal("Účet B", second.AccountName);
                Assert.Equal(100_000m, second.Amount);
                Assert.Equal(2.55m, second.NetAnnualRate);
            });
        Assert.Equal(0, plan.UnallocatedAmount);
    }

    [Fact]
    public void PlanSavingsAllocation_FillsRfLimitBeforeSendingRemainderToCsob()
    {
        var accounts = new[]
        {
            new SavingsAccount
            {
                Id = 1,
                Name = "RF sporenie",
                BankName = "RF banka",
                InterestTaxPercent = 0,
                RateTiers =
                [
                    new SavingsRateTier { FromAmount = 0, ToAmount = 500_000m, BaseAnnualRate = 4m },
                    new SavingsRateTier { FromAmount = 500_000m, ToAmount = null, BaseAnnualRate = 1m }
                ]
            },
            new SavingsAccount
            {
                Id = 2,
                Name = "ČSOB sporenie",
                BankName = "ČSOB",
                InterestTaxPercent = 0,
                RateTiers = [new SavingsRateTier { FromAmount = 0, ToAmount = null, BaseAnnualRate = 3m }]
            }
        };

        var plan = FinancialCalculator.PlanSavingsAllocation(accounts, 700_000m);

        Assert.Collection(
            plan.Allocations,
            first =>
            {
                Assert.Equal("RF banka", first.BankName);
                Assert.Equal(500_000m, first.Amount);
            },
            second =>
            {
                Assert.Equal("ČSOB", second.BankName);
                Assert.Equal(200_000m, second.Amount);
            });
    }

    [Fact]
    public void PlanSavingsAllocation_ChoosesHighestRateAfterTax()
    {
        var accounts = new[]
        {
            new SavingsAccount
            {
                Id = 1,
                Name = "Vyššia hrubá sadzba",
                InterestTaxPercent = 50m,
                RateTiers = [new SavingsRateTier { FromAmount = 0, ToAmount = null, BaseAnnualRate = 4m }]
            },
            new SavingsAccount
            {
                Id = 2,
                Name = "Vyššia čistá sadzba",
                InterestTaxPercent = 0m,
                RateTiers = [new SavingsRateTier { FromAmount = 0, ToAmount = null, BaseAnnualRate = 3m }]
            }
        };

        var plan = FinancialCalculator.PlanSavingsAllocation(accounts, 10_000m);

        var allocation = Assert.Single(plan.Allocations);
        Assert.Equal("Vyššia čistá sadzba", allocation.AccountName);
        Assert.Equal(10_000m, allocation.Amount);
        Assert.Equal(3m, allocation.NetAnnualRate);
    }

    [Fact]
    public void CalculateMortgage_ReturnsExpectedAnnuityPayment()
    {
        var plan = new MortgagePlan
        {
            PropertyPrice = 8_000_000m,
            OwnFunds = 800_000m,
            AnnualInterestRate = 4.59m,
            TermYears = 30
        };

        var result = FinancialCalculator.CalculateMortgage(plan);

        Assert.Equal(7_200_000m, result.Principal);
        Assert.InRange(result.MonthlyPayment, 36_850m, 36_950m);
        Assert.Equal(90m, result.LtvPercent);
    }

    [Fact]
    public void CalculateMortgagePaymentForMonth_StartsAtFirstPaymentAndStopsAfterTerm()
    {
        var plan = new MortgagePlan
        {
            PropertyPrice = 1_200_000m,
            OwnFunds = 0,
            AnnualInterestRate = 0,
            TermYears = 1,
            FirstPaymentDate = new DateTime(2026, 7, 15)
        };

        Assert.Equal(0, FinancialCalculator.CalculateMortgagePaymentForMonth(plan, new DateTime(2026, 6, 1)));
        Assert.Equal(100_000m, FinancialCalculator.CalculateMortgagePaymentForMonth(plan, new DateTime(2026, 7, 1)));
        Assert.Equal(100_000m, FinancialCalculator.CalculateMortgagePaymentForMonth(plan, new DateTime(2027, 6, 1)));
        Assert.Equal(0, FinancialCalculator.CalculateMortgagePaymentForMonth(plan, new DateTime(2027, 7, 1)));
    }

    [Fact]
    public void ProjectCashFlow_IncludesMortgageFromFirstPaymentMonth()
    {
        var plan = new MortgagePlan
        {
            PropertyPrice = 1_200_000m,
            OwnFunds = 0,
            AnnualInterestRate = 0,
            TermYears = 1,
            FirstPaymentDate = new DateTime(2026, 8, 1)
        };

        var projection = FinancialCalculator.ProjectCashFlow(
            [],
            new DateTime(2026, 7, 1),
            plan,
            months: 2);

        Assert.Equal(0, projection[0].Expenses);
        Assert.Equal(100_000m, projection[1].Expenses);
        Assert.Equal(100_000m, projection[1].MortgagePayment);
    }

    [Fact]
    public void ProjectCashFlow_SavesConfiguredPercentageOfPositiveSurplus()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            },
            new CashFlowEntry
            {
                Type = CashFlowType.Expense,
                MonthlyAmount = 30_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };

        var projection = FinancialCalculator.ProjectCashFlow(
            entries,
            new DateTime(2026, 1, 1),
            savingsRatePercent: 50m,
            months: 2);

        Assert.All(projection, row =>
        {
            Assert.Equal(70_000m, row.FreeBeforeSavings);
            Assert.Equal(35_000m, row.SavingsContribution);
            Assert.Equal(35_000m, row.FreeAmount);
        });
        Assert.Equal(70_000m, projection[1].CumulativeFreeAmount);
    }

    [Fact]
    public void ProjectCashFlow_SplitsPositiveSurplusBetweenSavingsAndInvestments()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            },
            new CashFlowEntry
            {
                Type = CashFlowType.Expense,
                MonthlyAmount = 30_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };

        var row = Assert.Single(FinancialCalculator.ProjectCashFlow(
            entries,
            new DateTime(2026, 1, 1),
            savingsRatePercent: 30m,
            investmentRatePercent: 40m,
            months: 1));

        Assert.Equal(21_000m, row.SavingsContribution);
        Assert.Equal(28_000m, row.InvestmentContribution);
        Assert.Equal(21_000m, row.FreeAmount);
    }

    [Fact]
    public void ProjectCashFlow_LimitsCombinedAllocationToFullPositiveSurplus()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };

        var row = Assert.Single(FinancialCalculator.ProjectCashFlow(
            entries,
            new DateTime(2026, 1, 1),
            savingsRatePercent: 80m,
            investmentRatePercent: 40m,
            months: 1));

        Assert.Equal(80_000m, row.SavingsContribution);
        Assert.Equal(20_000m, row.InvestmentContribution);
        Assert.Equal(0m, row.FreeAmount);
    }

    [Fact]
    public void ProjectCashFlow_DefaultsToSavingAllPositiveSurplus()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            },
            new CashFlowEntry
            {
                Type = CashFlowType.Expense,
                MonthlyAmount = 120_000m,
                IntervalMonths = null,
                FirstOccurrenceMonth = new DateTime(2026, 2, 1)
            }
        };

        var projection = FinancialCalculator.ProjectCashFlow(entries, new DateTime(2026, 1, 1), months: 2);

        Assert.Equal(100_000m, projection[0].SavingsContribution);
        Assert.Equal(0m, projection[0].FreeAmount);
        Assert.Equal(0m, projection[1].SavingsContribution);
        Assert.Equal(-20_000m, projection[1].FreeAmount);
    }

    [Fact]
    public void EstimateFreeCashByDate_SumsCashFlowThroughSelectedMonth()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };

        var estimate = FinancialCalculator.EstimateFreeCashByDate(
            entries,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 3, 15),
            mortgagePlan: null,
            savingsRatePercent: 25m);

        Assert.Equal(225_000m, estimate);
    }

    [Fact]
    public void ForecastFinancialPosition_IncludesSavingsWhenAllSurplusIsSaved()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            },
            new CashFlowEntry
            {
                Type = CashFlowType.Expense,
                MonthlyAmount = 40_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };
        var accounts = new[]
        {
            new SavingsAccount
            {
                Id = 1,
                Balance = 100_000m,
                RateTiers = [new SavingsRateTier { FromAmount = 0, ToAmount = null, BaseAnnualRate = 0 }]
            }
        };

        var forecast = FinancialCalculator.ForecastFinancialPosition(
            entries,
            accounts,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 2, 1),
            mortgagePlan: null,
            savingsRatePercent: 100m);

        Assert.Equal(220_000m, forecast.SavingsBalance);
        Assert.Equal(0m, forecast.CashOutsideSavings);
        Assert.Equal(220_000m, forecast.TotalAvailable);
        Assert.Equal(120_000m, forecast.SavingsDeposits);
    }

    [Fact]
    public void ForecastFinancialPosition_KeepsUnallocatedSavingsAsCash()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };

        var forecast = FinancialCalculator.ForecastFinancialPosition(
            entries,
            [],
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 1),
            mortgagePlan: null,
            savingsRatePercent: 100m);

        Assert.Equal(0m, forecast.SavingsBalance);
        Assert.Equal(100_000m, forecast.CashOutsideSavings);
        Assert.Equal(100_000m, forecast.TotalAvailable);
    }

    [Fact]
    public void ForecastFinancialPosition_TracksInvestmentsWithoutReturns()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            },
            new CashFlowEntry
            {
                Type = CashFlowType.Expense,
                MonthlyAmount = 40_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };
        var accounts = new[]
        {
            new SavingsAccount
            {
                Id = 1,
                Balance = 100_000m,
                RateTiers = [new SavingsRateTier { FromAmount = 0, ToAmount = null, BaseAnnualRate = 0 }]
            }
        };

        var forecast = FinancialCalculator.ForecastFinancialPosition(
            entries,
            accounts,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 2, 1),
            mortgagePlan: null,
            savingsRatePercent: 50m,
            investmentRatePercent: 30m);

        Assert.Equal(160_000m, forecast.SavingsBalance);
        Assert.Equal(36_000m, forecast.InvestmentBalance);
        Assert.Equal(24_000m, forecast.CashOutsideSavings);
        Assert.Equal(220_000m, forecast.TotalAvailable);
        Assert.Equal(0m, forecast.NetInterest);
    }

    [Fact]
    public void ForecastFinancialPosition_AddsFutureDepositsToCurrentInvestmentBalance()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };

        var forecast = FinancialCalculator.ForecastFinancialPosition(
            entries,
            [],
            new DateTime(2026, 1, 1),
            new DateTime(2026, 2, 1),
            mortgagePlan: null,
            savingsRatePercent: 50m,
            investmentRatePercent: 50m,
            currentInvestmentBalance: 250_000m);

        Assert.Equal(350_000m, forecast.InvestmentBalance);
        Assert.Equal(100_000m, forecast.InvestmentDeposits);
        Assert.Equal(450_000m, forecast.TotalAvailable);
    }

    [Fact]
    public void ProjectFinancialPosition_CalculatesInterestBeforePlannedMonthlyDeposit()
    {
        var entries = new[]
        {
            new CashFlowEntry
            {
                Type = CashFlowType.Income,
                MonthlyAmount = 100_000m,
                IntervalMonths = 1,
                FirstOccurrenceMonth = new DateTime(2026, 1, 1)
            }
        };
        var accounts = new[]
        {
            new SavingsAccount
            {
                Id = 1,
                Balance = 100_000m,
                InterestTaxPercent = 0,
                RateTiers = [new SavingsRateTier { FromAmount = 0, ToAmount = null, BaseAnnualRate = 12m }]
            }
        };

        var projection = FinancialCalculator.ProjectFinancialPosition(
            entries,
            accounts,
            new DateTime(2026, 1, 1),
            mortgagePlan: null,
            savingsRatePercent: 100m,
            months: 1);

        var row = Assert.Single(projection);
        Assert.Equal(1_000m, row.NetInterest);
        Assert.Equal(201_000m, row.SavingsBalance);
        Assert.Equal(201_000m, row.TotalAvailable);
    }
}
