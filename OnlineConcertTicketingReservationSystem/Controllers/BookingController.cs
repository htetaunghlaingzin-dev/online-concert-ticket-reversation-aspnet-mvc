using OnlineConcertTicketingReservationSystem.Data;
using OnlineConcertTicketingReservationSystem.Models;
using OnlineConcertTicketingReservationSystem.Models.Enums;
using OnlineConcertTicketingReservationSystem.Models.ViewModels;
using OnlineConcertTicketingReservationSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OnlineConcertTicketingReservationSystem.Controllers;

[Authorize]
public class BookingController : Controller
{
    private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxSlipSizeBytes = 5 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SeatNotifier _notifier;
    private readonly IWebHostEnvironment _env;

    public BookingController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, SeatNotifier notifier, IWebHostEnvironment env)
    {
        _db = db;
        _userManager = userManager;
        _notifier = notifier;
        _env = env;
    }

    private Guid CurrentUserId => Guid.Parse(_userManager.GetUserId(User)!);

    [HttpGet]
    public IActionResult SelectSeats(int concertId) => RedirectToAction(nameof(SelectTickets), new { concertId });

    [HttpGet]
    public async Task<IActionResult> SelectTickets(int concertId)
    {
        var concert = await _db.Concerts.Include(c => c.TicketTypes).Include(c => c.Accessories).FirstOrDefaultAsync(c => c.Id == concertId);
        if (concert is null) return NotFound();
        return View(new TicketSelectionViewModel { Concert = concert, ErrorMessage = TempData["Error"] as string });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult ReserveSeats(ReserveSeatsRequest request)
    {
        TempData["Error"] = "Booking now uses ticket types. Please choose your tickets.";
        return RedirectToAction(nameof(SelectTickets), new { concertId = request.ConcertId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReserveTickets(ReserveTicketsRequest request)
    {
        IActionResult Invalid(string error)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(SelectTickets), new { concertId = request.ConcertId });
        }
        if (!ModelState.IsValid || request.Quantity < 1 || request.Quantity > 100) return Invalid("Choose a ticket type and a quantity between 1 and 100.");
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(_db);
        var type = await _db.TicketTypes.Include(t => t.Concert).FirstOrDefaultAsync(t => t.Id == request.TicketTypeId && t.ConcertId == request.ConcertId);
        if (type is null || !new[] { "VIP", "GA", "VVIP" }.Contains(type.Name)) return Invalid("Choose a valid ticket type for this concert.");
        if (type.Concert.EventDate <= DateTime.UtcNow) return Invalid("Booking has closed for this concert.");
        if (type.AvailableStock < request.Quantity) return Invalid(type.AvailableStock == 0 ? "Sold Out. Please choose another ticket type." : $"Only {type.AvailableStock} tickets are available.");
        var order = new Order { UserId = CurrentUserId, ConcertId = request.ConcertId, TicketTypeId = type.Id,
            TicketTypeName = type.Name, Quantity = request.Quantity, UnitPrice = type.Price, TotalAmount = type.Price * request.Quantity };
        if (request.Accessories.Any(a => a.Quantity < 0 || a.Quantity > 100) || request.Accessories.GroupBy(a => a.AccessoryId).Any(g => g.Count() > 1))
            return Invalid("Invalid merchandise quantities.");
        foreach (var selection in request.Accessories.Where(a => a.Quantity > 0))
        {
            var item = await _db.Accessories.FirstOrDefaultAsync(a => a.Id == selection.AccessoryId && a.ConcertId == request.ConcertId);
            if (item is null || item.StockQuantity < selection.Quantity) return Invalid("The selected merchandise quantity is unavailable.");
            order.OrderAccessories.Add(new OrderAccessory { AccessoryId = item.Id, UnitPrice = item.Price, Quantity = selection.Quantity });
            order.TotalAmount += item.Price * selection.Quantity;
        }
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Checkout), new { orderId = order.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Checkout(int orderId)
    {
        var order = await _db.Orders.Include(o => o.Concert).Include(o => o.OrderAccessories).ThenInclude(oa => oa.Accessory)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == CurrentUserId);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.PendingPayment) return RedirectToAction(nameof(Confirmation), new { orderId });
        var summaries = new List<string>();
        if (order.TicketTypeId.HasValue) summaries.Add($"{order.TicketTypeName} × {order.Quantity} — MMK {order.UnitPrice:N2} each");
        else
        {
            var seats = await _db.Seats.Include(s => s.TicketType).Where(s => s.CurrentOrderId == order.Id).ToListAsync();
            summaries.AddRange(seats.Select(s => $"{s.TicketType?.Name ?? "Legacy ticket"} — MMK {s.Price:N2}"));
        }
        return View(new CheckoutViewModel { OrderId = order.Id, ConcertId = order.ConcertId, ConcertTitle = order.Concert.Title,
            TotalAmount = order.TotalAmount, TicketSummaries = summaries, CanPay = order.LockExpiresAt == null || order.LockExpiresAt > DateTime.UtcNow,
            AccessorySummaries = order.OrderAccessories.Select(oa => $"{oa.Accessory.Name} × {oa.Quantity} (MMK {oa.UnitPrice:N2} each)").ToList(),
            ErrorMessage = TempData["Error"] as string });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxSlipSizeBytes)]
    public async Task<IActionResult> UploadSlip(UploadSlipRequest request)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(_db);
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId);
        if (order is null || order.UserId != CurrentUserId)
        {
            return NotFound();
        }

        if (order.Status != OrderStatus.PendingPayment || order.LockExpiresAt <= DateTime.UtcNow)
        {
            TempData["Error"] = "Your reservation has expired.";
            return RedirectToAction(nameof(Checkout), new { orderId = order.Id });
        }

        if (string.IsNullOrWhiteSpace(request.TransactionRefId) || request.TransactionRefId.Length > 100)
        {
            TempData["Error"] = "A transaction reference is required.";
            return RedirectToAction(nameof(Checkout), new { orderId = order.Id });
        }
        request.TransactionRefId = request.TransactionRefId.Trim();
        var validationError = await ValidateSlipFileAsync(request.SlipImage);
        if (validationError is not null)
        {
            TempData["Error"] = validationError;
            return RedirectToAction(nameof(Checkout), new { orderId = order.Id });
        }

        if (await _db.Orders.AnyAsync(o => o.TransactionRefId == request.TransactionRefId))
        {
            TempData["Error"] = "This transaction reference ID has already been used.";
            return RedirectToAction(nameof(Checkout), new { orderId = order.Id });
        }

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "slips");
        Directory.CreateDirectory(uploadsDir);

        var extension = Path.GetExtension(request.SlipImage.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsDir, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await request.SlipImage.CopyToAsync(stream);
        }

        order.PaymentSlipUrl = $"/uploads/slips/{fileName}";
        order.TransactionRefId = request.TransactionRefId;
        order.Status = OrderStatus.PendingApproval;

        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "This transaction reference ID has already been used.";
            return RedirectToAction(nameof(Checkout), new { orderId = order.Id });
        }

        return RedirectToAction(nameof(Confirmation), new { orderId = order.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Confirmation(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Concert)
            .Include(o => o.Tickets).ThenInclude(t => t.Seat).ThenInclude(s => s!.TicketType)
            .Include(o => o.OrderAccessories).ThenInclude(oa => oa.Accessory)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null || order.UserId != CurrentUserId)
        {
            return NotFound();
        }

        return View(order);
    }

    private static async Task<string?> ValidateSlipFileAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return "Please select a payment slip image to upload.";
        }

        if (file.Length > MaxSlipSizeBytes)
        {
            return "The uploaded file exceeds the 5MB size limit.";
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
        {
            return "Only JPG, PNG, and WEBP images are allowed.";
        }

        var header = new byte[12];
        await using (var stream = file.OpenReadStream())
        {
            var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
            if (read < 4)
            {
                return "Invalid image file.";
            }
        }

        var isJpeg = header[0] == 0xFF && header[1] == 0xD8;
        var isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        var isWebp = header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;

        if (!isJpeg && !isPng && !isWebp)
        {
            return "The file content does not match a supported image format.";
        }

        return null;
    }
}
