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

        var now = DateTime.UtcNow;
        var expiredOrders = await db.Orders
            .Where(o => o.Status == OrderStatus.PendingPayment && o.LockExpiresAt != null && o.LockExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (expiredOrders.Count == 0)
        {
            return;
        }

        foreach (var order in expiredOrders)
        {
            order.Status = OrderStatus.Expired;

            var seats = await db.Seats
                .Where(s => s.CurrentOrderId == order.Id)
                .ToListAsync(cancellationToken);

            foreach (var seat in seats)
            {
                seat.Status = SeatStatus.Available;
                seat.CurrentOrderId = null;
            }

            await db.SaveChangesAsync(cancellationToken);

            foreach (var seat in seats)
            {
                await notifier.NotifySeatStatusAsync(order.ConcertId, seat.Id, SeatStatus.Available);
            }

            _logger.LogInformation("Order {OrderId} expired and its seats were released.", order.Id);
        }
    }
}
