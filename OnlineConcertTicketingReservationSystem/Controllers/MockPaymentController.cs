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
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(_db);
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

        await transaction.CommitAsync();
        return View(payment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int paymentId, bool isSuccessful)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(_db);
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
            TempData["Error"] = "Mock payment was declined. You can try again; availability will be checked when payment succeeds.";
            return RedirectToAction(nameof(Pay), new { orderId = order.Id });
        }

        var error = await TicketInventory.ConfirmAsync(_db, order);
        if (error is not null)
        {
            await transaction.RollbackAsync();
            TempData["Error"] = error;
            return RedirectToAction("Checkout", "Booking", new { orderId = order.Id });
        }
        payment.Status = PaymentStatus.Succeeded;
        payment.CompletedAt = DateTime.UtcNow;
        order.TransactionRefId = payment.Reference;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return RedirectToAction("Confirmation", "Booking", new { orderId = order.Id });
    }
}
