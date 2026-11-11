using Helpdesk.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Helpdesk.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a real SQL Server. By default a container is started; set HELPDESK_TEST_SQL to a
/// connection string (for example LocalDB) to use an existing server instead.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string ServerOverrideVariable = "HELPDESK_TEST_SQL";

    private readonly List<string> _databases = [];
    private MsSqlContainer? _container;
    private string _serverConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var overrideConnection = Environment.GetEnvironmentVariable(ServerOverrideVariable);

        if (!string.IsNullOrWhiteSpace(overrideConnection))
        {
            _serverConnectionString = overrideConnection;
            return;
        }

        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        _serverConnectionString = _container.GetConnectionString();
    }

    /// <summary>Creates a new empty database with the current migrations applied.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"HelpdeskTests_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = name }.ConnectionString;

        var options = new DbContextOptionsBuilder<HelpdeskDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new HelpdeskDbContext(options);
        await db.Database.MigrateAsync();

        _databases.Add(name);
        return connectionString;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        foreach (var name in _databases)
        {
            var builder = new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await command.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerTestGroup : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}
