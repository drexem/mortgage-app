using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MortgageApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<CashFlowEntry> CashFlowEntries => Set<CashFlowEntry>();
    public DbSet<SavingsAccount> SavingsAccounts => Set<SavingsAccount>();
    public DbSet<SavingsRateTier> SavingsRateTiers => Set<SavingsRateTier>();
    public DbSet<MortgagePlan> MortgagePlans => Set<MortgagePlan>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<CashFlowEntry>().HasIndex(x => x.UserId);
        builder.Entity<SavingsAccount>().HasIndex(x => x.UserId);
        builder.Entity<MortgagePlan>().HasIndex(x => x.UserId).IsUnique();

        builder.Entity<SavingsRateTier>()
            .HasOne(x => x.SavingsAccount)
            .WithMany(x => x.RateTiers)
            .HasForeignKey(x => x.SavingsAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
