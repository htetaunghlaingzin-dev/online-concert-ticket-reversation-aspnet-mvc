using OnlineConcertTicketingReservationSystem.Data;
using OnlineConcertTicketingReservationSystem.Models;
using OnlineConcertTicketingReservationSystem.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineConcertTicketingReservationSystem.Services;

namespace OnlineConcertTicketingReservationSystem.Controllers;

[Authorize(Roles = DbSeeder.AdminRole)]
public class ManagementController : Controller
{
    private readonly ApplicationDbContext _db;
    public ManagementController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var concerts = await _db.Concerts.Include(c => c.Venue).Include(c => c.ConcertArtists).ThenInclude(ca => ca.Artist).OrderBy(c => c.EventDate).ToListAsync();
        var ticketTypes = await _db.TicketTypes.Include(t => t.Concert).OrderBy(t => t.Concert.EventDate).ToListAsync();

        return View("Index", new ManagementDashboardViewModel
        {
            Venues = await _db.Venues.OrderBy(v => v.Name).ToListAsync(),
            Artists = await _db.Artists.OrderBy(a => a.Name).ToListAsync(),
            Concerts = concerts,
            TicketTypes = ticketTypes,
            Schedules = await _db.ConcertSchedules.Include(s => s.Concert).OrderBy(s => s.StartsAt).ToListAsync(),
            Accessories = await _db.Accessories.Include(a => a.Concert).OrderBy(a => a.Concert.Title).ToListAsync(),
            Message = TempData["Message"] as string
        });
    }

    [HttpGet]
    public Task<IActionResult> CreateVenue() => Index();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateVenue(string name, string? address, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name) || capacity <= 0) return Back("Venue name and a positive capacity are required.");
        _db.Venues.Add(new Venue { Name = name.Trim(), Address = address?.Trim() ?? string.Empty, Capacity = capacity });
        await _db.SaveChangesAsync(); return Back("Venue added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateArtist(string name, string? genre)
    {
        if (string.IsNullOrWhiteSpace(name)) return Back("Artist name is required.");
        _db.Artists.Add(new Artist { Name = name.Trim(), Genre = genre?.Trim() });
        await _db.SaveChangesAsync(); return Back("Artist added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateConcert(string title, int? venueId, DateTime eventDate, VenueType venueType, string? trailerUrl)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(title) || eventDate == default || !Enum.IsDefined(venueType)) return Back("Concert title and date are required.");
        if (venueId is not null && !await _db.Venues.AnyAsync(v => v.Id == venueId)) return Back("Selected venue does not exist.");
        if (!IsValidUrl(trailerUrl)) return Back("Trailer URL must be a valid http(s) link.");
        _db.Concerts.Add(new Concert { Title = title.Trim(), VenueId = venueId, EventDate = eventDate.ToUniversalTime(), VenueType = venueType, TrailerUrl = string.IsNullOrWhiteSpace(trailerUrl) ? null : trailerUrl.Trim() });
        await _db.SaveChangesAsync(); return Back("Concert added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateConcertTrailer(int concertId, string? trailerUrl)
    {
        if (!IsValidUrl(trailerUrl)) return Back("Trailer URL must be a valid http(s) link.");
        var concert = await _db.Concerts.FindAsync(concertId);
        if (concert is null) return Back("Concert not found.");
        concert.TrailerUrl = string.IsNullOrWhiteSpace(trailerUrl) ? null : trailerUrl.Trim();
        await _db.SaveChangesAsync(); return Back("Trailer updated.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddArtist(int concertId, int artistId, int performanceOrder)
    {
        if (await _db.ConcertArtists.AnyAsync(x => x.ConcertId == concertId && x.ArtistId == artistId)) return Back("Artist is already on this concert.");
        _db.ConcertArtists.Add(new ConcertArtist { ConcertId = concertId, ArtistId = artistId, PerformanceOrder = performanceOrder });
        await _db.SaveChangesAsync(); return Back("Artist added to concert.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSchedule(int concertId, DateTime startsAt, DateTime endsAt)
    {
        if (endsAt <= startsAt) return Back("Schedule end time must be after start time.");
        _db.ConcertSchedules.Add(new ConcertSchedule { ConcertId = concertId, StartsAt = startsAt.ToUniversalTime(), EndsAt = endsAt.ToUniversalTime() });
        await _db.SaveChangesAsync(); return Back("Schedule added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTicketType(int concertId, string name, decimal price, int availableStock)
    {
        if (!ModelState.IsValid || !new[] { "VIP", "GA", "VVIP" }.Contains(name) || price < 0 || price > 999999999m || decimal.Round(price, 2) != price || availableStock < 0)
            return Back("Choose VIP, GA, or VVIP, a non-negative MMK price (up to two decimals), and available stock.");
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(_db);
        if (!await _db.Concerts.AnyAsync(c => c.Id == concertId)) return Back("Selected concert does not exist.");
        if (await _db.TicketTypes.AnyAsync(t => t.ConcertId == concertId && t.Name == name)) return Back("This ticket type already exists. Update its price and stock below.");
        _db.TicketTypes.Add(new TicketType { ConcertId = concertId, Name = name, Price = price, Capacity = availableStock, AvailableStock = availableStock });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Back("Ticket type added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTicketType(int id, decimal price, int availableStock, string rowVersion)
    {
        if (!ModelState.IsValid || price < 0 || price > 999999999m || decimal.Round(price, 2) != price || availableStock < 0)
            return Back("Enter a non-negative MMK price (up to two decimals) and available stock.");
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(_db);
        var type = await _db.TicketTypes.FindAsync(id);
        if (type is null) return Back("Ticket type not found.");
        if (Convert.ToBase64String(type.RowVersion) != rowVersion)
            return Back("Stock or price changed while you were editing. Review the latest values and try again.");
        type.Price = price;
        type.AvailableStock = availableStock;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Back("Ticket price and available stock updated.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateConcert(int concertId, string title, int? venueId, DateTime eventDate, VenueType venueType, string? trailerUrl)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(title) || eventDate == default || !Enum.IsDefined(venueType) || !IsValidUrl(trailerUrl))
            return Back("Enter a title, valid date, venue type, and optional trailer URL.");
        if (venueId.HasValue && !await _db.Venues.AnyAsync(v => v.Id == venueId)) return Back("Selected venue does not exist.");
        var concert = await _db.Concerts.FindAsync(concertId);
        if (concert is null) return Back("Concert not found.");
        concert.Title = title.Trim(); concert.VenueId = venueId; concert.EventDate = eventDate.ToUniversalTime();
        concert.VenueType = venueType; concert.TrailerUrl = string.IsNullOrWhiteSpace(trailerUrl) ? null : trailerUrl.Trim();
        await _db.SaveChangesAsync(); return Back("Concert updated.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string entity, int id)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(_db);
        object? item = entity switch { "venue" => await _db.Venues.FindAsync(id), "artist" => await _db.Artists.FindAsync(id), "concert" => await _db.Concerts.FindAsync(id), "schedule" => await _db.ConcertSchedules.FindAsync(id), "ticketType" => await _db.TicketTypes.FindAsync(id), "accessory" => await _db.Accessories.FindAsync(id), _ => null };
        if (item is null) return Back("Item not found.");
        _db.Remove(item);
        try { await _db.SaveChangesAsync(); await transaction.CommitAsync(); return Back("Item deleted."); }
        catch (DbUpdateException) { return Back("This item is in use and cannot be deleted."); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAccessory(int concertId, string name, decimal price, int stockQuantity, string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(name) || price < 0 || stockQuantity < 0) return Back("Merch item requires a name, non-negative price, and stock quantity.");
        if (!await _db.Concerts.AnyAsync(c => c.Id == concertId)) return Back("Selected concert does not exist.");
        if (!IsValidUrl(imageUrl)) return Back("Image URL must be a valid http(s) link.");
        _db.Accessories.Add(new Accessory { ConcertId = concertId, Name = name.Trim(), Price = price, StockQuantity = stockQuantity, ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim() });
        await _db.SaveChangesAsync(); return Back("Merch item added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RestockAccessory(int id, int stockQuantity, string? imageUrl)
    {
        if (stockQuantity < 0) return Back("Stock quantity cannot be negative.");
        if (!IsValidUrl(imageUrl)) return Back("Image URL must be a valid http(s) link.");
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(_db);
        var accessory = await _db.Accessories.FindAsync(id);
        if (accessory is null) return Back("Merch item not found.");
        accessory.StockQuantity = stockQuantity;
        if (!string.IsNullOrWhiteSpace(imageUrl)) accessory.ImageUrl = imageUrl.Trim();
        await _db.SaveChangesAsync(); await transaction.CommitAsync(); return Back("Stock updated.");
    }

    private static bool IsValidUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) || (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps));

    private IActionResult Back(string message) { TempData["Message"] = message; return RedirectToAction(nameof(Index)); }
}

public class ManagementDashboardViewModel
{
    public List<Venue> Venues { get; set; } = new();
    public List<Artist> Artists { get; set; } = new();
    public List<Concert> Concerts { get; set; } = new();
    public List<ConcertSchedule> Schedules { get; set; } = new();
    public List<TicketType> TicketTypes { get; set; } = new();
    public List<Accessory> Accessories { get; set; } = new();
    public string? Message { get; set; }
}
