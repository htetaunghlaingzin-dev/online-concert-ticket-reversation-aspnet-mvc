using OnlineConcertTicketingReservationSystem.Data;
using OnlineConcertTicketingReservationSystem.Models;
using OnlineConcertTicketingReservationSystem.Models.Enums;
using OnlineConcertTicketingReservationSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OnlineConcertTicketingReservationSystem.Controllers;

[Authorize]
public class MockPaymentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SeatNotifier _notifier;

    public MockPaymentController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, SeatNotifier notifier)
    {
        _db = db;
        _userManager = userManager;
        _notifier = notifier;
    }

    private Guid CurrentUserId => Guid.Parse(_userManager.GetUserId(User)!);

    [HttpGet]
    public async Task<IActionResult> Pay(int orderId)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == CurrentUserId);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.PendingPayment) return RedirectToAction("Confirmation", "Booking", new { orderId });

        var payment = await _db.PaymentTransactions
            .FirstOrDefaultAsync(p => p.OrderId == orderId && p.Status == PaymentStatus.Pending);
        if (payment is null)
        {
            payment = new PaymentTransaction
            {
                OrderId = order.Id,
                Amount = order.TotalAmount,
                Reference = $"MOCK-{Guid.NewGuid():N}".ToUpperInvariant()
            };
            _db.PaymentTransactions.Add(payment);
            await _db.SaveChangesAsync();
        }

        return View(payment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int paymentId, bool isSuccessful)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var payment = await _db.PaymentTransactions
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.Order.UserId == CurrentUserId);
        if (payment is null) return NotFound();
        if (payment.Status != PaymentStatus.Pending) return RedirectToAction("Confirmation", "Booking", new { orderId = payment.OrderId });

        var order = payment.Order;
        if (order.Status != OrderStatus.PendingPayment || order.LockExpiresAt <= DateTime.UtcNow)
        {
            payment.Status = PaymentStatus.Failed;
            payment.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Error"] = "This reservation has expired.";
            return RedirectToAction("Checkout", "Booking", new { orderId = order.Id });
        }

        if (!isSuccessful)
        {
            payment.Status = PaymentStatus.Failed;
            payment.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Error"] = "Mock payment was declined. Your reservation remains active until it expires.";
            return RedirectToAction(nameof(Pay), new { orderId = order.Id });
        }

        var seats = await _db.Seats.Where(s => s.CurrentOrderId == order.Id).ToListAsync();
        var orderAccessories = await _db.OrderAccessories
            .Include(oa => oa.Accessory)
            .Where(oa => oa.OrderId == order.Id)
            .ToListAsync();
        payment.Status = PaymentStatus.Succeeded;
        payment.CompletedAt = DateTime.UtcNow;
        order.Status = OrderStatus.Confirmed;
        order.LockExpiresAt = null;
        order.TransactionRefId = payment.Reference;
        foreach (var seat in seats)
        {
            seat.Status = SeatStatus.Booked;
            _db.Tickets.Add(new Ticket { OrderId = order.Id, SeatId = seat.Id, TicketCode = $"TKT-{Guid.NewGuid():N}".ToUpperInvariant() });
        }
        foreach (var item in orderAccessories)
            item.Accessory.StockQuantity = Math.Max(0, item.Accessory.StockQuantity - item.Quantity);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        foreach (var seat in seats)
            await _notifier.NotifySeatStatusAsync(order.ConcertId, seat.Id, SeatStatus.Booked);

        return RedirectToAction("Confirmation", "Booking", new { orderId = order.Id });
    }
}
