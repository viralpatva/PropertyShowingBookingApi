using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShowingBooking.Application.Abstractions;
using ShowingBooking.Infrastructure.Repositories;

namespace ShowingBooking.Infrastructure;

public static class DependencyInjection
{
    public const string DefaultConnectionString = "Data Source=showingbooking.db";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // The connection string is read when the context is created, not when the app is wired up,
        // so tests can point each test run at its own database file.
        services.AddDbContext<ShowingDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>()
                .GetConnectionString("Default") ?? DefaultConnectionString;
            options.UseSqlite(connectionString);
        });

        // Same scoped DbContext behind all three, so they share one unit of work per request.
        services.AddScoped<ISlotRepository, SlotRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddHostedService<DatabaseInitializer>();
        return services;
    }
}

/// <summary>
/// Runs once when the host starts: creates the database if needed and switches SQLite to WAL mode,
/// which lets readers keep working while a write is in progress. No migrations: see README.
/// </summary>
internal sealed class DatabaseInitializer(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShowingDbContext>();

        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
