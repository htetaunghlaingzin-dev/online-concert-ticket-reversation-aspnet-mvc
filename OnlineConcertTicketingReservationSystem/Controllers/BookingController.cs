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
    public async Task<IActionResult> SelectSeats(int concertId)
    {
        var concert = await _db.Concerts.FindAsync(concertId);
        if (concert is null)
        {
            return NotFound();
        }

        var seats = await _db.Seats
            .Include(s => s.TicketType)
            .Where(s => s.ConcertId == concertId)
            .OrderBy(s => s.Section).ThenBy(s => s.Row).ThenBy(s => s.SeatNumber)
            .Select(s => new SeatViewModel
            {
                Id = s.Id,
                Section = s.Section,
                Row = s.Row,
                SeatNumber = s.SeatNumber,
                Price = s.Price,
                Status = s.Status,
                TicketTypeName = s.TicketType == null ? null : s.TicketType.Name
            })
            .ToListAsync();

        var ticketTypeLegend = await _db.TicketTypes
            .Where(t => t.ConcertId == concertId)
            .OrderByDescending(t => t.Price)
            .Select(t => new TicketTypeLegendItem { Name = t.Name, Price = t.Price })
            .ToListAsync();

        var accessories = await _db.Accessories
            .Where(a => a.ConcertId == concertId)
            .Select(a => new AccessoryViewModel
            {
                Id = a.Id,
                Name = a.Name,
                Price = a.Price,
                StockQuantity = a.StockQuantity,
                ImageUrl = a.ImageUrl
            })
            .ToListAsync();

        var model = new SeatSelectionViewModel
        {
            ConcertId = concert.Id,
            ConcertTitle = concert.Title,
            EventDate = concert.EventDate,
            Seats = seats,
            Accessories = accessories,
            TicketTypeLegend = ticketTypeLegend,
            ErrorMessage = TempData["Error"] as string,
            ConflictingSeatIds = TempData["ConflictingSeatIds"] is string csv && csv.Length > 0
                ? csv.Split(',').Select(int.Parse).ToList()
                : new List<int>()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReserveSeats(ReserveSeatsRequest request)
    {
        if (request.SeatIds.Count == 0)
        {
            TempData["Error"] = "Please select at least one seat.";
            return RedirectToAction(nameof(SelectSeats), new { concertId = request.ConcertId });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var seats = await _db.Seats
                .Where(s => request.SeatIds.Contains(s.Id) && s.ConcertId == request.ConcertId)
                .ToListAsync();

            var unavailable = seats.Where(s => s.Status != SeatStatus.Available).Select(s => s.Id).ToList();
            if (unavailable.Count > 0 || seats.Count != request.SeatIds.Count)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "One or more selected seats are no longer available.";
                TempData["ConflictingSeatIds"] = string.Join(',', unavailable);
                return RedirectToAction(nameof(SelectSeats), new { concertId = request.ConcertId });
            }

            var accessoryIds = request.Accessories.Where(a => a.Quantity > 0).Select(a => a.AccessoryId).ToList();
            var accessories = await _db.Accessories
                .Where(a => accessoryIds.Contains(a.Id) && a.ConcertId == request.ConcertId)
                .ToListAsync();

            var seatTotal = seats.Sum(s => s.Price);
            var order = new Order
            {
                UserId = CurrentUserId,
                ConcertId = request.ConcertId,
                Status = OrderStatus.PendingPayment,
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10),
                CreatedAt = DateTime.UtcNow
            };

            decimal accessoryTotal = 0m;
            foreach (var selection in request.Accessories.Where(a => a.Quantity > 0))
            {
                var accessory = accessories.FirstOrDefault(a => a.Id == selection.AccessoryId);
                if (accessory is null)
                {
                    continue;
                }

                var quantity = Math.Min(selection.Quantity, accessory.StockQuantity);
                if (quantity <= 0)
                {
                    continue;
                }

                accessoryTotal += accessory.Price * quantity;
                order.OrderAccessories.Add(new OrderAccessory
                {
                    AccessoryId = accessory.Id,
                    Quantity = quantity,
                    UnitPrice = accessory.Price
                });
            }

            order.TotalAmount = seatTotal + accessoryTotal;
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            foreach (var seat in seats)
            {
                seat.Status = SeatStatus.PendingPayment;
                seat.CurrentOrderId = order.Id;
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            foreach (var seat in seats)
            {
                await _notifier.NotifySeatStatusAsync(request.ConcertId, seat.Id, SeatStatus.PendingPayment);
            }

            return RedirectToAction(nameof(Checkout), new { orderId = order.Id });
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            TempData["Error"] = "One or more selected seats were just taken by another user. Please reselect.";
            return RedirectToAction(nameof(SelectSeats), new { concertId = request.ConcertId });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Checkout(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Concert)
            .Include(o => o.OrderAccessories).ThenInclude(oa => oa.Accessory)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null || order.UserId != CurrentUserId)
        {
            return NotFound();
        }

        var seats = await _db.Seats.Where(s => s.CurrentOrderId == order.Id).ToListAsync();

        var model = new CheckoutViewModel
        {
            OrderId = order.Id,
            ConcertTitle = order.Concert.Title,
            TotalAmount = order.TotalAmount,
            LockExpiresAt = order.LockExpiresAt,
            SeatSummaries = seats.Select(s => $"Section {s.Section}, Row {s.Row}, Seat {s.SeatNumber} (${s.Price})").ToList(),
            AccessorySummaries = order.OrderAccessories.Select(oa => $"{oa.Accessory.Name} x{oa.Quantity} (${oa.UnitPrice} each)").ToList(),
            ErrorMessage = TempData["Error"] as string
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxSlipSizeBytes)]
    public async Task<IActionResult> UploadSlip(UploadSlipRequest request)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId);
        if (order is null || order.UserId != CurrentUserId)
        {
            return NotFound();
        }

        if (order.Status != OrderStatus.PendingPayment || order.LockExpiresAt is null || order.LockExpiresAt <= DateTime.UtcNow)
        {
            TempData["Error"] = "Your reservation has expired.";
            return RedirectToAction(nameof(Checkout), new { orderId = order.Id });
        }

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
            .Include(o => o.Tickets).ThenInclude(t => t.Seat)
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
