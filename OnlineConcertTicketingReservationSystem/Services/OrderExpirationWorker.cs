using OnlineConcertTicketingReservationSystem.Data;
using OnlineConcertTicketingReservationSystem.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace OnlineConcertTicketingReservationSystem.Services;

public class OrderExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderExpirationWorker> _logger;

    public OrderExpirationWorker(IServiceScopeFactory scopeFactory, ILogger<OrderExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await ExpireOrdersOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while expiring pending orders.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExpireOrdersOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<SeatNotifier>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await TicketInventory.LockAsync(db);
        var now = DateTime.UtcNow;
        var expiredOrders = await db.Orders
            .Where(o => o.Status == OrderStatus.PendingPayment && o.LockExpiresAt != null && o.LockExpiresAt <= now)
            .ToListAsync(cancellationToken);
        foreach (var order in expiredOrders)
        {
            order.Status = OrderStatus.Expired;
            await TicketInventory.ReleaseLegacySeatsAsync(db, order.Id);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
