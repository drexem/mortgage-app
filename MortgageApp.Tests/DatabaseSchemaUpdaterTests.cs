using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MortgageApp.Data;

namespace MortgageApp.Tests;

public class DatabaseSchemaUpdaterTests
{
    [Fact]
    public async Task UpdateAsync_AddsMortgageMarkerToCashFlowEntries()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE CashFlowEntries DROP COLUMN IsMortgageRelated");
            await DatabaseSchemaUpdater.UpdateAsync(db);
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(CashFlowEntries)";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            Assert.Contains("IsMortgageRelated", columns);
        }

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task UpdateAsync_AddsInvestmentSettingsToExistingUsers()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers DROP COLUMN InvestmentRatePercent");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers DROP COLUMN InvestmentBalance");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers DROP COLUMN InvestmentBalanceUpdatedAtUtc");

            await DatabaseSchemaUpdater.UpdateAsync(db);
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(AspNetUsers)";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            Assert.Contains("InvestmentRatePercent", columns);
            Assert.Contains("InvestmentBalance", columns);
            Assert.Contains("InvestmentBalanceUpdatedAtUtc", columns);
        }

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task UpdateAsync_AddsSavingsBalanceConfirmationDate()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE SavingsAccounts DROP COLUMN BalanceUpdatedAtUtc");
            await DatabaseSchemaUpdater.UpdateAsync(db);
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(SavingsAccounts)";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            Assert.Contains("BalanceUpdatedAtUtc", columns);
        }

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task UpdateAsync_MakesIntervalOptionalAndPreservesExistingEntries()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE CashFlowEntries");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE CashFlowEntries (
                    Id INTEGER NOT NULL CONSTRAINT PK_CashFlowEntries PRIMARY KEY AUTOINCREMENT,
                    UserId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    MonthlyAmount TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    IntervalMonths INTEGER NOT NULL DEFAULT 1,
                    FirstOccurrenceMonth TEXT NOT NULL
                )
                """);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO CashFlowEntries
                    (UserId, Name, MonthlyAmount, Type, IntervalMonths, FirstOccurrenceMonth)
                VALUES
                    ('user-1', 'Existing salary', '100000.0', 0, 1, '2026-01-01 00:00:00')
                """);

            await DatabaseSchemaUpdater.UpdateAsync(db);
        }

        await using (var db = new ApplicationDbContext(options))
        {
            db.CashFlowEntries.Add(new CashFlowEntry
            {
                UserId = "user-1",
                Name = "One-time bonus",
                MonthlyAmount = 50_000m,
                Type = CashFlowType.Income,
                IntervalMonths = null,
                FirstOccurrenceMonth = new DateTime(2026, 7, 1)
            });
            await db.SaveChangesAsync();

            var entries = await db.CashFlowEntries.OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.Equal(1, entries[0].IntervalMonths);
            Assert.Null(entries[1].IntervalMonths);
        }

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task UpdateAsync_RemovesLegacyMonthlyContributionAndPreservesSavingsData()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.SavingsAccounts.Add(new SavingsAccount
            {
                UserId = "user-1",
                Name = "Existing savings",
                BankName = "Existing bank",
                Balance = 25_000m,
                RateTiers =
                [
                    new SavingsRateTier
                    {
                        FromAmount = 0,
                        ToAmount = null,
                        BaseAnnualRate = 3m,
                        BonusAnnualRate = 1m,
                        SortOrder = 1
                    }
                ]
            });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE SavingsAccounts ADD COLUMN MonthlyContribution TEXT NOT NULL DEFAULT '0.0'");

            await DatabaseSchemaUpdater.UpdateAsync(db);
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var existing = await db.SavingsAccounts.Include(x => x.RateTiers).SingleAsync();
            Assert.Equal("Existing savings", existing.Name);
            Assert.Equal(25_000m, existing.Balance);
            Assert.Single(existing.RateTiers);

            db.SavingsAccounts.Add(new SavingsAccount { UserId = "user-1", Name = "New savings" });
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.SavingsAccounts.CountAsync());
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(SavingsAccounts)";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            Assert.DoesNotContain("MonthlyContribution", columns);
        }

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }
}
