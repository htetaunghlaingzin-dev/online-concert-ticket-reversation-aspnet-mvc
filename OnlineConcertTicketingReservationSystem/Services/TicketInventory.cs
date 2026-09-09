using Microsoft.EntityFrameworkCore;
using OnlineConcertTicketingReservationSystem.Data;
using OnlineConcertTicketingReservationSystem.Models;
using OnlineConcertTicketingReservationSystem.Models.Enums;

namespace OnlineConcertTicketingReservationSystem.Services;

public static class TicketInventory
{
    // A database-owned lock works across application instances. Call inside a transaction,
    // before loading mutable orders or inventory. It is released on commit/rollback.
    public static Task LockAsync(ApplicationDbContext db) => db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource=N'ConcertTicketInventory',
            @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
        IF @result < 0 THROW 51000, 'Inventory is busy. Please try again.', 1;
        """);

    // The caller commits payment, order, inventory and issued tickets in one transaction.
    public static async Task<string?> ConfirmAsync(ApplicationDbContext db, Order order)
    {
        if (order.Status == OrderStatus.Confirmed) return null;
        if (order.Status is not (OrderStatus.PendingPayment or OrderStatus.PendingApproval))
            return "This order can no longer be paid.";
        if (order.Status == OrderStatus.PendingPayment && order.LockExpiresAt <= DateTime.UtcNow)
            return "This older reservation has expired. Please choose tickets again.";

        var items = await db.OrderAccessories.Include(x => x.Accessory).Where(x => x.OrderId == order.Id).ToListAsync();
        if (items.Any(x => x.Quantity <= 0 || x.Accessory.StockQuantity < x.Quantity))
            return "Some merchandise is no longer available. Please start a new booking.";

        var seats = await db.Seats.Where(s => s.CurrentOrderId == order.Id).ToListAsync();
        if (order.TicketTypeId is int typeId)
        {
            if (!await db.Concerts.AnyAsync(c => c.Id == order.ConcertId && c.EventDate > DateTime.UtcNow))
                return "Booking has closed for this concert. Please choose another concert.";
            if (order.StockDeducted || order.Quantity <= 0) return "This order cannot be processed again.";
            var changed = await db.TicketTypes.Where(t => t.Id == typeId && t.ConcertId == order.ConcertId && t.AvailableStock >= order.Quantity)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.AvailableStock, t => t.AvailableStock - order.Quantity));
            if (changed != 1) return "There are not enough tickets available. Please choose a smaller quantity or another ticket type.";
            order.StockDeducted = true;
            for (var i = 0; i < order.Quantity; i++)
                db.Tickets.Add(new Ticket { OrderId = order.Id, TicketTypeId = typeId, TicketTypeName = order.TicketTypeName,
                    UnitPrice = order.UnitPrice, TicketCode = $"TKT-{Guid.NewGuid():N}".ToUpperInvariant() });
        }
        else
        {
            // Legacy reservations were excluded from migrated available stock already.
            if (seats.Count == 0 || seats.Any(s => s.Status != SeatStatus.PendingPayment))
                return "This older reservation is no longer available. Please choose tickets again.";
            foreach (var seat in seats)
            {
                seat.Status = SeatStatus.Booked;
                if (!await db.Tickets.AnyAsync(t => t.OrderId == order.Id && t.SeatId == seat.Id))
                    db.Tickets.Add(new Ticket { OrderId = order.Id, SeatId = seat.Id, UnitPrice = seat.Price,
                        TicketCode = $"TKT-{Guid.NewGuid():N}".ToUpperInvariant() });
            }
        }
        foreach (var item in items) item.Accessory.StockQuantity -= item.Quantity;
        order.Status = OrderStatus.Confirmed;
        order.LockExpiresAt = null;
        return null;
    }

    public static async Task ReleaseTicketAsync(ApplicationDbContext db, Ticket ticket)
    {
        if (ticket.RevokedAt != null) return;
        ticket.RevokedAt = DateTime.UtcNow;
        var typeId = ticket.TicketTypeId ?? ticket.Seat?.TicketTypeId;
        if (typeId.HasValue)
            await db.TicketTypes.Where(t => t.Id == typeId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AvailableStock, t => t.AvailableStock + 1));
        if (ticket.Seat is not null)
        {
            ticket.Seat.Status = SeatStatus.Available;
            ticket.Seat.CurrentOrderId = null;
        }
    }

    public static async Task ReleaseLegacySeatsAsync(ApplicationDbContext db, int orderId)
    {
        var seats = await db.Seats.Where(s => s.CurrentOrderId == orderId && s.Status == SeatStatus.PendingPayment).ToListAsync();
        foreach (var seat in seats)
        {
            if (seat.TicketTypeId is int typeId)
                await db.TicketTypes.Where(t => t.Id == typeId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AvailableStock, t => t.AvailableStock + 1));
            seat.Status = SeatStatus.Available;
            seat.CurrentOrderId = null;
        }
    }
}
