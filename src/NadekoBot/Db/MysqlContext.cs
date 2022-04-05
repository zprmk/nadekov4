using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Services.Database;

public sealed class MysqlContext : NadekoContext
{
    private readonly string _connStr;
    private readonly string _version;

    protected override string CurrencyTransactionOtherIdDefaultValue
        => "NULL";
    protected override string DiscordUserLastXpGainDefaultValue
        => "(UTC_TIMESTAMP - INTERVAL 1 year)";
    protected override string LastLevelUpDefaultValue
        => "(UTC_TIMESTAMP)";

    public MysqlContext(string connStr = "Server=localhost", string version = "8.0")
    {
        _connStr = connStr;
        _version = version;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder
            .UseLowerCaseNamingConvention()
            .UseMySql(_connStr, ServerVersion.Parse(_version));
    }
}