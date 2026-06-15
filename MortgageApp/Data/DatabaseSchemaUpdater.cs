using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace MortgageApp.Data;

public static class DatabaseSchemaUpdater
{
    public static async Task UpdateAsync(ApplicationDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        var savingsColumns = await GetColumnsAsync(connection, "SavingsAccounts");
        if (!savingsColumns.ContainsKey("BankName"))
        {
            await ExecuteAsync(connection, "ALTER TABLE SavingsAccounts ADD COLUMN BankName TEXT NOT NULL DEFAULT ''");
        }

        if (!savingsColumns.ContainsKey("BalanceUpdatedAtUtc"))
        {
            await ExecuteAsync(connection, "ALTER TABLE SavingsAccounts ADD COLUMN BalanceUpdatedAtUtc TEXT NULL");
        }

        if (savingsColumns.ContainsKey("MonthlyContribution"))
        {
            await RebuildSavingsAccountsAsync(connection);
        }

        var userColumns = await GetColumnsAsync(connection, "AspNetUsers");
        if (!userColumns.ContainsKey("SavingsRatePercent"))
        {
            await ExecuteAsync(connection, "ALTER TABLE AspNetUsers ADD COLUMN SavingsRatePercent TEXT NOT NULL DEFAULT '100.0'");
        }

        if (!userColumns.ContainsKey("InvestmentRatePercent"))
        {
            await ExecuteAsync(connection, "ALTER TABLE AspNetUsers ADD COLUMN InvestmentRatePercent TEXT NOT NULL DEFAULT '0.0'");
        }

        if (!userColumns.ContainsKey("InvestmentBalance"))
        {
            await ExecuteAsync(connection, "ALTER TABLE AspNetUsers ADD COLUMN InvestmentBalance TEXT NOT NULL DEFAULT '0.0'");
        }

        if (!userColumns.ContainsKey("InvestmentBalanceUpdatedAtUtc"))
        {
            await ExecuteAsync(connection, "ALTER TABLE AspNetUsers ADD COLUMN InvestmentBalanceUpdatedAtUtc TEXT NULL");
        }

        var cashFlowColumns = await GetColumnsAsync(connection, "CashFlowEntries");
        if (!cashFlowColumns.ContainsKey("IntervalMonths"))
        {
            await ExecuteAsync(connection, "ALTER TABLE CashFlowEntries ADD COLUMN IntervalMonths INTEGER NULL");
        }

        if (!cashFlowColumns.ContainsKey("FirstOccurrenceMonth"))
        {
            await ExecuteAsync(connection, "ALTER TABLE CashFlowEntries ADD COLUMN FirstOccurrenceMonth TEXT NOT NULL DEFAULT '2000-01-01 00:00:00'");
        }

        if (!cashFlowColumns.ContainsKey("IsMortgageRelated"))
        {
            await ExecuteAsync(connection, "ALTER TABLE CashFlowEntries ADD COLUMN IsMortgageRelated INTEGER NOT NULL DEFAULT 0");
        }

        cashFlowColumns = await GetColumnsAsync(connection, "CashFlowEntries");
        if (cashFlowColumns["IntervalMonths"].IsRequired)
        {
            await RebuildCashFlowEntriesAsync(connection);
        }

        var mortgageColumns = await GetColumnsAsync(connection, "MortgagePlans");
        if (!mortgageColumns.ContainsKey("FirstPaymentDate"))
        {
            await ExecuteAsync(connection, "ALTER TABLE MortgagePlans ADD COLUMN FirstPaymentDate TEXT NOT NULL DEFAULT '2099-01-01 00:00:00'");
        }
    }

    private static async Task RebuildCashFlowEntriesAsync(DbConnection connection)
    {
        await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF");
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await ExecuteAsync(connection, """
                CREATE TABLE CashFlowEntries_New (
                    Id INTEGER NOT NULL CONSTRAINT PK_CashFlowEntries PRIMARY KEY AUTOINCREMENT,
                    UserId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    MonthlyAmount TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    IsMortgageRelated INTEGER NOT NULL,
                    IntervalMonths INTEGER NULL,
                    FirstOccurrenceMonth TEXT NOT NULL
                )
                """, transaction);
            await ExecuteAsync(connection, """
                INSERT INTO CashFlowEntries_New
                    (Id, UserId, Name, MonthlyAmount, Type, IsMortgageRelated, IntervalMonths, FirstOccurrenceMonth)
                SELECT
                    Id, UserId, Name, MonthlyAmount, Type, IsMortgageRelated, IntervalMonths, FirstOccurrenceMonth
                FROM CashFlowEntries
                """, transaction);
            await ExecuteAsync(connection, "DROP TABLE CashFlowEntries", transaction);
            await ExecuteAsync(connection, "ALTER TABLE CashFlowEntries_New RENAME TO CashFlowEntries", transaction);
            await ExecuteAsync(connection, "CREATE INDEX IX_CashFlowEntries_UserId ON CashFlowEntries (UserId)", transaction);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON");
        }
    }

    private static async Task RebuildSavingsAccountsAsync(DbConnection connection)
    {
        await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF");
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await ExecuteAsync(connection, """
                CREATE TABLE SavingsAccounts_New (
                    Id INTEGER NOT NULL CONSTRAINT PK_SavingsAccounts PRIMARY KEY AUTOINCREMENT,
                    UserId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    BankName TEXT NOT NULL,
                    Balance TEXT NOT NULL,
                    BalanceUpdatedAtUtc TEXT NULL,
                    InterestTaxPercent TEXT NOT NULL,
                    MeetsBonusConditions INTEGER NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                )
                """, transaction);
            await ExecuteAsync(connection, """
                INSERT INTO SavingsAccounts_New
                    (Id, UserId, Name, BankName, Balance, BalanceUpdatedAtUtc, InterestTaxPercent, MeetsBonusConditions, UpdatedAtUtc)
                SELECT
                    Id, UserId, Name, BankName, Balance, BalanceUpdatedAtUtc, InterestTaxPercent, MeetsBonusConditions, UpdatedAtUtc
                FROM SavingsAccounts
                """, transaction);
            await ExecuteAsync(connection, "DROP TABLE SavingsAccounts", transaction);
            await ExecuteAsync(connection, "ALTER TABLE SavingsAccounts_New RENAME TO SavingsAccounts", transaction);
            await ExecuteAsync(connection, "CREATE INDEX IX_SavingsAccounts_UserId ON SavingsAccounts (UserId)", transaction);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON");
        }
    }

    private static async Task<Dictionary<string, ColumnInfo>> GetColumnsAsync(
        DbConnection connection,
        string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            columns[name] = new ColumnInfo(name, reader.GetInt32(3) == 1);
        }

        return columns;
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        string sql,
        DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record ColumnInfo(string Name, bool IsRequired);
}
