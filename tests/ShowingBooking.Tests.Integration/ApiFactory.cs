using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ShowingBooking.Tests.Integration;

/// <summary>A clock the tests can move. Safe to read from many threads at once.</summary>
public sealed class FakeClock : TimeProvider
{
    private long _utcTicks;

    public FakeClock(DateTimeOffset now) => SetNow(now);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void SetNow(DateTimeOffset now) => Interlocked.Exchange(ref _utcTicks, now.UtcTicks);
}

/// <summary>
/// Starts the real app in memory, backed by its own SQLite file (not :memory:, because parallel
/// requests need separate connections to see the same data) and the controllable clock.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"showing-tests-{Guid.NewGuid():N}.db");

    public FakeClock Clock { get; } = new(ApiTestBase.Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databasePath}");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing) return;

        // Pooled connections keep the file open, so release them before deleting.
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_databasePath + suffix); }
            catch (IOException) { /* temp file, best effort */ }
        }
    }
}
