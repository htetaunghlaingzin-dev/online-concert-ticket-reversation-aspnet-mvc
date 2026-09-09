using OnlineConcertTicketingReservationSystem.Data;
using OnlineConcertTicketingReservationSystem.Models.Enums;
using OnlineConcertTicketingReservationSystem.Models.ViewModels;
using OnlineConcertTicketingReservationSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OnlineConcertTicketingReservationSystem.Controllers;

[Authorize(Roles = DbSeeder.AdminRole)]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly SeatNotifier _notifier;

    public AdminController(ApplicationDbContext db, SeatNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var now = DateTime.UtcNow;

        var totalRevenue = await _db.Orders.Where(o => o.Status == OrderStatus.Confirmed).SumAsync(o => o.TotalAmount);
        var ticketsSold = await _db.Tickets.CountAsync(t => t.Order.Status == OrderStatus.Confirmed && t.RevokedAt == null);
        var pendingApprovalCount = await _db.Orders.CountAsync(o => o.Status == OrderStatus.PendingApproval);
        var upcomingConcertsCount = await _db.Concerts.CountAsync(c => c.EventDate >= now);

        var lowStockAccessories = await _db.Accessories
            .Include(a => a.Concert)
            .Where(a => a.StockQuantity < 10)
            .OrderBy(a => a.StockQuantity)
            .Select(a => new LowStockAccessoryViewModel { Id = a.Id, ConcertTitle = a.Concert.Title, Name = a.Name, StockQuantity = a.StockQuantity })
            .ToListAsync();

        var recentOrders = await _db.Orders
            .Include(o => o.User)
            .Include(o => o.Concert)
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .Select(o => new OrderSummaryViewModel
            {
                Id = o.Id,
                ConcertTitle = o.Concert.Title,
                UserName = o.User.Name,
                UserEmail = o.User.Email ?? string.Empty,
                Status = o.Status,
                TotalAmount = o.TotalAmount,
                CreatedAt = o.CreatedAt
            })
            .ToListAsync();

        return View(new AdminDashboardViewModel
        {
            TotalRevenue = totalRevenue,
            TicketsSold = ticketsSold,
            PendingApprovalCount = pendingApprovalCount,
            UpcomingConcertsCount = upcomingConcertsCount,
            LowStockAccessories = lowStockAccessories,
            RecentOrders = recentOrders
        });
    }

    [HttpGet]
    public async Task<IActionResult> Orders(OrderStatus? status)
    {
        var query = _db.Orders.Include(o => o.User).Include(o => o.Concert).AsQueryable();
        if (status is not null) query = query.Where(o => o.Status == status);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryViewModel
            {
                Id = o.Id,
                ConcertTitle = o.Concert.Title,
                UserName = o.User.Name,
                UserEmail = o.User.Email ?? string.Empty,
                Status = o.Status,
                TotalAmount = o.TotalAmount,
                CreatedAt = o.CreatedAt
            })
            .ToListAsync();

        ViewBag.StatusFilter = status;
        return View(orders);
    }

    [HttpGet]
    public async Task<IActionResult> OrderDetail(int id, bool popup = false)
    {
        var order = await _db.Orders
            .Include(o => o.PaymentTransactions)
            .Include(o => o.User)
            .Include(o => o.Concert)
            .Include(o => o.OrderAccessories).ThenInclude(oa => oa.Accessory)
            .Include(o => o.Tickets).ThenInclude(t => t.Seat).ThenInclude(s => s!.TicketType)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return NotFound();

        var summaries = new List<OrderTicketSummary>();
        if (order.TicketTypeId.HasValue)
            summaries.Add(new OrderTicketSummary { Name = order.TicketTypeName ?? "Ticket", Quantity = order.Quantity, UnitPrice = order.UnitPrice });
        else
        {
            var legacy = order.Tickets.Count > 0 ? order.Tickets.Where(t => t.Seat != null).Select(t => t.Seat!).ToList()
                : await _db.Seats.Include(s => s.TicketType).Where(s => s.CurrentOrderId == order.Id).ToListAsync();
            summaries = legacy.GroupBy(s => new { Name = s.TicketType?.Name ?? "Legacy ticket", s.Price })
                .Select(g => new OrderTicketSummary { Name = g.Key.Name, Quantity = g.Count(), UnitPrice = g.Key.Price }).ToList();
        }
        var model = new OrderDetailViewModel
        {
            Id = order.Id,
            TicketSummary = summaries,
            PaymentStatus = order.Status == OrderStatus.Confirmed ? "Paid"
                : order.PaymentTransactions.Any(p => p.Status == PaymentStatus.Refunded) ? "Refunded"
                : order.Status == OrderStatus.PendingApproval ? "Awaiting verification"
                : order.PaymentTransactions.OrderByDescending(p => p.Id).FirstOrDefault()?.Status.ToString() ?? "Not paid",
            ConcertTitle = order.Concert.Title,
            UserName = order.User.Name,
            UserEmail = order.User.Email ?? string.Empty,
            Status = order.Status,
            TotalAmount = order.TotalAmount,
            CreatedAt = order.CreatedAt,
            LockExpiresAt = order.LockExpiresAt,
            TransactionRefId = order.TransactionRefId,
            RejectionReason = order.RejectionReason,
            CancellationReason = order.CancellationReason,
            CancelledAt = order.CancelledAt,
            Tickets = order.Tickets.Select(t => new TicketLineViewModel
            {
                Id = t.Id,
                SeatLabel = t.DisplayType,
                TicketCode = t.TicketCode,
                Price = t.DisplayPrice,
                Revoked = t.RevokedAt is not null,
                RevocationReason = t.RevocationReason
            }).ToList(),
            AccessorySummaries = order.OrderAccessories.Select(oa => $"{oa.Accessory.Name} x{oa.Quantity} (MMK {oa.UnitPrice:N2} each)").ToList()
        };

        TempData.TryGetValue("Message", out var message);
        ViewBag.Message = message as string;
        return popup ? PartialView("_OrderDetails", model) : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(CancelOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
        {
            TempData["Message"] = "A cancellation reason is required.";
            return RedirectToAction(nameof(OrderDetail), new { id = request.OrderId });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        await TicketInventory.LockAsync(_db);
        var order = await _db.Orders
            .Include(o => o.Tickets).ThenInclude(t => t.Seat).ThenInclude(s => s!.TicketType)
            .Include(o => o.OrderAccessories).ThenInclude(oa => oa.Accessory)
            .Include(o => o.PaymentTransactions)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId);

        if (order is null) return NotFound();

        if (order.Status != OrderStatus.Confirmed)
        {
            TempData["Message"] = "Only confirmed orders can be cancelled.";
            return RedirectToAction(nameof(OrderDetail), new { id = request.OrderId });
        }

        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = request.Reason.Trim();
        order.CancelledAt = DateTime.UtcNow;

        foreach (var ticket in order.Tickets)
        {
            await TicketInventory.ReleaseTicketAsync(_db, ticket);
        }

        if (!order.Tickets.Any() && order.TicketTypeId == null)
        {
            var legacySeats = await _db.Seats.Where(s => s.CurrentOrderId == order.Id).ToListAsync();
            foreach (var seat in legacySeats)
            {
                if (seat.TicketTypeId is int typeId)
                    await _db.TicketTypes.Where(t => t.Id == typeId).ExecuteUpdateAsync(set => set.SetProperty(t => t.AvailableStock, t => t.AvailableStock + 1));
                seat.Status = SeatStatus.Available;
                seat.CurrentOrderId = null;
            }
        }
        foreach (var orderAccessory in order.OrderAccessories)
        {
            orderAccessory.Accessory.StockQuantity += orderAccessory.Quantity;
        }

        foreach (var payment in order.PaymentTransactions.Where(p => p.Status == PaymentStatus.Succeeded))
        {
            payment.Status = PaymentStatus.Refunded;
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        foreach (var ticket in order.Tickets)
        {
            if (ticket.Seat != null) await _notifier.NotifySeatStatusAsync(order.ConcertId, ticket.Seat.Id, SeatStatus.Available);
        }

        TempData["Message"] = "Order cancelled and stock restored.";
        return RedirectToAction(nameof(OrderDetail), new { id = request.OrderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeTicket(RevokeTicketRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
        {
            TempData["Message"] = "A revocation reason is required.";
            return RedirectToAction(nameof(OrderDetail), new { id = request.OrderId });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        await TicketInventory.LockAsync(_db);
        var ticket = await _db.Tickets
            .Include(t => t.Seat)
            .Include(t => t.Order).ThenInclude(o => o.Tickets)
            .Include(t => t.Order).ThenInclude(o => o.PaymentTransactions)
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && t.OrderId == request.OrderId);

        if (ticket is null) return NotFound();

        if (ticket.Order.Status != OrderStatus.Confirmed)
        {
            TempData["Message"] = "Only tickets on confirmed orders can be revoked.";
            return RedirectToAction(nameof(OrderDetail), new { id = request.OrderId });
        }

        if (ticket.RevokedAt is not null)
        {
            TempData["Message"] = "This ticket has already been revoked.";
            return RedirectToAction(nameof(OrderDetail), new { id = request.OrderId });
        }

        await TicketInventory.ReleaseTicketAsync(_db, ticket);
        ticket.RevocationReason = request.Reason.Trim();

        var allTicketsRevoked = ticket.Order.Tickets.All(t => t.RevokedAt is not null);
        if (allTicketsRevoked)
        {
            var items = await _db.OrderAccessories.Include(x => x.Accessory).Where(x => x.OrderId == ticket.OrderId).ToListAsync();
            foreach (var item in items) item.Accessory.StockQuantity += item.Quantity;
            ticket.Order.Status = OrderStatus.Cancelled;
            ticket.Order.CancellationReason = "All tickets in this order were individually revoked.";
            ticket.Order.CancelledAt = DateTime.UtcNow;

            foreach (var payment in ticket.Order.PaymentTransactions.Where(p => p.Status == PaymentStatus.Succeeded))
            {
                payment.Status = PaymentStatus.Refunded;
            }
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        if (ticket.Seat != null) await _notifier.NotifySeatStatusAsync(ticket.Order.ConcertId, ticket.Seat.Id, SeatStatus.Available);

        TempData["Message"] = allTicketsRevoked
            ? "Ticket revoked. All tickets on this order are now revoked, so the order was cancelled and the payment marked refunded."
            : "Ticket revoked and stock restored.";
        return RedirectToAction(nameof(OrderDetail), new { id = request.OrderId });
    }

    [HttpGet]
    public async Task<IActionResult> VerificationQueue()
    {
        var orders = await _db.Orders
            .Where(o => o.Status == OrderStatus.PendingApproval)
            .Include(o => o.User)
            .Include(o => o.Concert)
            .Include(o => o.OrderAccessories).ThenInclude(oa => oa.Accessory)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();
        var seatsByOrder = await _db.Seats
            .Where(s => s.CurrentOrderId != null && orderIds.Contains(s.CurrentOrderId.Value))
            .ToListAsync();

        var items = orders.Select(o => new VerificationQueueItemViewModel
        {
            OrderId = o.Id,
            UserName = o.User.Name,
            UserEmail = o.User.Email ?? string.Empty,
            ConcertTitle = o.Concert.Title,
            SeatLabels = o.TicketTypeId.HasValue ? new List<string> { $"{o.TicketTypeName} × {o.Quantity} — MMK {o.UnitPrice:N2} each" }
                : new List<string> { $"Legacy tickets × {seatsByOrder.Count(s => s.CurrentOrderId == o.Id)}" },
            AccessorySummaries = o.OrderAccessories.Select(oa => $"{oa.Accessory.Name} x{oa.Quantity}").ToList(),
            TotalAmount = o.TotalAmount,
            PaymentSlipUrl = o.PaymentSlipUrl,
            TransactionRefId = o.TransactionRefId,
            CreatedAt = o.CreatedAt
        }).ToList();

        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOrder(VerifyOrderRequest request)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        await TicketInventory.LockAsync(_db);
        var order = await _db.Orders
            .Include(o => o.OrderAccessories).ThenInclude(oa => oa.Accessory)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId);

        if (order is null || order.Status != OrderStatus.PendingApproval)
        {
            return NotFound();
        }

        var seats = await _db.Seats.Where(s => s.CurrentOrderId == order.Id).ToListAsync();

        if (request.IsApproved)
        {
            var error = await TicketInventory.ConfirmAsync(_db, order);
            if (error is not null)
            {
                await transaction.RollbackAsync();
                TempData["Message"] = error;
                return RedirectToAction(nameof(VerificationQueue));
            }
            _db.PaymentTransactions.Add(new OnlineConcertTicketingReservationSystem.Models.PaymentTransaction
            {
                OrderId = order.Id, Amount = order.TotalAmount, Reference = $"SLIP-{order.Id}-{Guid.NewGuid():N}",
                Status = PaymentStatus.Succeeded, CompletedAt = DateTime.UtcNow
            });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.RejectionReason))
            {
                ModelState.AddModelError(string.Empty, "A rejection reason is required.");
                TempData["Message"] = "A rejection reason is required.";
                return RedirectToAction(nameof(VerificationQueue));
            }

            order.Status = OrderStatus.Rejected;
            order.RejectionReason = request.RejectionReason;

            await TicketInventory.ReleaseLegacySeatsAsync(_db, order.Id);

        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var newStatus = request.IsApproved ? SeatStatus.Booked : SeatStatus.Available;
        foreach (var seat in seats)
        {
            await _notifier.NotifySeatStatusAsync(order.ConcertId, seat.Id, newStatus);
        }

        return RedirectToAction(nameof(VerificationQueue));
    }
}
