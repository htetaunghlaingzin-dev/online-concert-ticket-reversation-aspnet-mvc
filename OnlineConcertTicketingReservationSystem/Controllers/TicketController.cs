using OnlineConcertTicketingReservationSystem.Data;
using OnlineConcertTicketingReservationSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace OnlineConcertTicketingReservationSystem.Controllers;

[Authorize]
public class TicketController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    public TicketController(ApplicationDbContext db, UserManager<ApplicationUser> users) { _db = db; _users = users; }
    private Guid UserId => Guid.Parse(_users.GetUserId(User)!);

    public async Task<IActionResult> MyTickets() => View(await _db.Tickets.Include(t => t.Seat).ThenInclude(s => s!.TicketType).Include(t => t.Order).ThenInclude(o => o.Concert)
        .Where(t => t.Order.UserId == UserId).OrderByDescending(t => t.IssuedAt).ToListAsync());

    public async Task<IActionResult> Qr(int id)
    {
        var ticket = await _db.Tickets.Include(t => t.Order).FirstOrDefaultAsync(t => t.Id == id && t.Order.UserId == UserId);
        if (ticket is null) return NotFound();
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(ticket.TicketCode, QRCodeGenerator.ECCLevel.Q);
        return Content(new SvgQRCode(data).GetGraphic(5), "image/svg+xml");
    }

    [Authorize(Roles = "Admin")]
    public IActionResult Validate() => View();

    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Validate(string ticketCode)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await OnlineConcertTicketingReservationSystem.Services.TicketInventory.LockAsync(_db);
        var ticket = await _db.Tickets.Include(t => t.Seat).ThenInclude(s => s!.TicketType).Include(t => t.Order).ThenInclude(o => o.Concert)
            .FirstOrDefaultAsync(t => t.TicketCode == ticketCode);
        if (ticket is null) { ViewBag.Result = "Ticket not found."; return View(); }
        if (ticket.Order.Status != OnlineConcertTicketingReservationSystem.Models.Enums.OrderStatus.Confirmed) { ViewBag.Result = "This order is not confirmed."; return View(); }
        if (ticket.RevokedAt is not null) { ViewBag.Result = "This ticket was cancelled and is no longer valid."; return View(); }
        if (ticket.ValidatedAt is not null) { ViewBag.Result = $"Already used at {ticket.ValidatedAt.Value.ToLocalTime():g}."; return View(); }
        ticket.ValidatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        ViewBag.Result = $"Valid ticket: {ticket.Order.Concert.Title}, {ticket.DisplayType}.";
        return View();
    }
}
