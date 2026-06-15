using Microsoft.AspNetCore.Identity;

namespace MortgageApp.Data;

public class ApplicationUser : IdentityUser
{
    public string PreferredCurrency { get; set; } = "CZK";
    public decimal SavingsRatePercent { get; set; } = 100m;
    public decimal InvestmentRatePercent { get; set; }
    public decimal InvestmentBalance { get; set; }
    public DateTime? InvestmentBalanceUpdatedAtUtc { get; set; }
}
