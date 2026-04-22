using SqlSugar;

namespace InfoCompareAssistant.Services;

public static class SqlSugarFactory
{
    public static SqlSugarClient CreateClient(string databasePath)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={databasePath}",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
    }
}
