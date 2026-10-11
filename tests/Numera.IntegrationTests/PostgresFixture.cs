using Microsoft.EntityFrameworkCore;
using Npgsql;
using Numera.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Numera.IntegrationTests;

/// <summary>One real PostgreSQL container shared by a test class; each test gets its own migrated database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateDatabaseAsync(bool migrate = true)
    {
        var name = $"numera_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
        if (migrate)
        {
            await using var db = CreateContext(connectionString);
            await db.Database.MigrateAsync();
        }

        return connectionString;
    }

    public static NumeraDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<NumeraDbContext>().UseNpgsql(connectionString).Options);
}
