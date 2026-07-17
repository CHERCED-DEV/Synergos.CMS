using Microsoft.Data.Sqlite;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="SqliteMaintenanceHostedService.RunPragmasAsync"/>
/// (Cap-270 Batch D, Ola 279). Cubre el path concreto del PRAGMA
/// execution sobre una DB SQLite real (in-memory shared cache para
/// que WAL pueda inicializarse + persistir).
/// </summary>
public sealed class SqliteMaintenanceHostedServiceTests : IDisposable
{
    private readonly string _tempDbPath;

    public SqliteMaintenanceHostedServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), "syn-sqlite-test-" + Guid.NewGuid().ToString("N") + ".db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (System.IO.File.Exists(_tempDbPath))
        {
            try { System.IO.File.Delete(_tempDbPath); } catch { /* best-effort */ }
            try { System.IO.File.Delete(_tempDbPath + "-wal"); } catch { }
            try { System.IO.File.Delete(_tempDbPath + "-shm"); } catch { }
        }
    }

    private string ConnectionString => $"Data Source={_tempDbPath};Cache=Shared";

    private async Task SeedAsync()
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync();
        // Switch to WAL mode (mismo que Umbraco 13 default).
        await using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        await pragma.ExecuteNonQueryAsync();

        await using var create = conn.CreateCommand();
        create.CommandText = "CREATE TABLE t1 (id INTEGER PRIMARY KEY, value TEXT);";
        await create.ExecuteNonQueryAsync();

        // Insert filas para forzar WAL pages.
        for (var i = 0; i < 50; i++)
        {
            await using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO t1 (value) VALUES ($v);";
            insert.Parameters.AddWithValue("$v", new string('x', 200));
            await insert.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task RunPragmasAsync_HappyPath_TruncatesWalAndReturnsBusy0()
    {
        await SeedAsync();

        var (busy, optimized) = await SqliteMaintenanceHostedService.RunPragmasAsync(
            ConnectionString, CancellationToken.None);

        // wal_checkpoint(TRUNCATE) returns busy=0 cuando todos los frames
        // se procesaron OK. Si hay otra connection abierta sería >0.
        Assert.Equal(0, busy);
        Assert.True(optimized);
    }

    [Fact]
    public async Task RunPragmasAsync_NoExceptionWhenWalAlreadySmall()
    {
        await SeedAsync();
        // Run twice: el segundo run debería ser un no-op effectively
        // (WAL ya truncado).
        await SqliteMaintenanceHostedService.RunPragmasAsync(ConnectionString, CancellationToken.None);
        var (busy, optimized) = await SqliteMaintenanceHostedService.RunPragmasAsync(
            ConnectionString, CancellationToken.None);

        Assert.Equal(0, busy);
        Assert.True(optimized);
    }

    [Fact]
    public async Task RunPragmasAsync_NewEmptyDb_DoesNotThrow()
    {
        // Antes del seed: DB no existe. SqliteConnection lo crea on demand.
        var (busy, optimized) = await SqliteMaintenanceHostedService.RunPragmasAsync(
            ConnectionString, CancellationToken.None);

        Assert.Equal(0, busy);
        Assert.True(optimized);
    }
}
