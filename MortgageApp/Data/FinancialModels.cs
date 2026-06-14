using System.ComponentModel.DataAnnotations;

namespace MortgageApp.Data;

public enum CashFlowType
{
    Income,
    Expense
}

public class CashFlowEntry
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = "";

    [Required, StringLength(80)]
    public string Name { get; set; } = "";

    [Range(0.01, double.MaxValue)]
    public decimal MonthlyAmount { get; set; }

    public CashFlowType Type { get; set; }

    [Range(1, 120)]
    public int? IntervalMonths { get; set; }

    public DateTime FirstOccurrenceMonth { get; set; } =
        new(DateTime.Today.Year, DateTime.Today.Month, 1);
}

public class SavingsAccount
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = "";

    [Required, StringLength(80)]
    public string Name { get; set; } = "Nový sporiaci účet";

    [StringLength(80)]
    public string BankName { get; set; } = "";

    [Range(0, double.MaxValue)]
    public decimal Balance { get; set; }

    [Range(0, 100)]
    public decimal InterestTaxPercent { get; set; } = 15m;

    public bool MeetsBonusConditions { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<SavingsRateTier> RateTiers { get; set; } = [];
}

public class SavingsRateTier
{
    public int Id { get; set; }
    public int SavingsAccountId { get; set; }
    public SavingsAccount? SavingsAccount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal FromAmount { get; set; }

    public decimal? ToAmount { get; set; }

    [Range(0, 100)]
    public decimal BaseAnnualRate { get; set; }

    [Range(0, 100)]
    public decimal BonusAnnualRate { get; set; }

    public int SortOrder { get; set; }
}

public class MortgagePlan
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = "";

    [Range(0, double.MaxValue)]
    public decimal PropertyPrice { get; set; } = 8_000_000m;

    [Range(0, double.MaxValue)]
    public decimal OwnFunds { get; set; } = 800_000m;

    [Range(0, 100)]
    public decimal AnnualInterestRate { get; set; } = 4.59m;

    [Range(1, 50)]
    public int TermYears { get; set; } = 30;

    public DateTime FirstPaymentDate { get; set; } = DateTime.Today.AddMonths(1);
}
