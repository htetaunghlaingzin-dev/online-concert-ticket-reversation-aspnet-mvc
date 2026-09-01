using OnlineConcertTicketingReservationSystem.Data;
using OnlineConcertTicketingReservationSystem.Models;
using OnlineConcertTicketingReservationSystem.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        var seatSections = await _db.Seats
            .Include(s => s.Concert)
            .Include(s => s.TicketType)
            .GroupBy(s => new { s.ConcertId, s.Concert.Title, s.Section })
            .Select(g => new SeatSectionSummary
            {
                ConcertId = g.Key.ConcertId,
                ConcertTitle = g.Key.Title,
                Section = g.Key.Section,
                SeatCount = g.Count(),
                MinPrice = g.Min(s => s.Price),
                MaxPrice = g.Max(s => s.Price),
                TicketTypeName = g.Select(s => s.TicketType == null ? "Unassigned" : s.TicketType.Name).Distinct().Count() == 1
                    ? g.Select(s => s.TicketType == null ? "Unassigned" : s.TicketType.Name).First()
                    : "Mixed"
            })
            .OrderBy(s => s.ConcertTitle).ThenBy(s => s.Section)
            .ToListAsync();

        return View(new ManagementDashboardViewModel
        {
            Venues = await _db.Venues.OrderBy(v => v.Name).ToListAsync(),
            Artists = await _db.Artists.OrderBy(a => a.Name).ToListAsync(),
            Concerts = concerts,
            TicketTypes = ticketTypes,
            Schedules = await _db.ConcertSchedules.Include(s => s.Concert).OrderBy(s => s.StartsAt).ToListAsync(),
            Accessories = await _db.Accessories.Include(a => a.Concert).OrderBy(a => a.Concert.Title).ToListAsync(),
            SeatSections = seatSections,
            Message = TempData["Message"] as string
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateVenue(string name, string address, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name) || capacity <= 0) return Back("Venue name and a positive capacity are required.");
        _db.Venues.Add(new Venue { Name = name.Trim(), Address = address.Trim(), Capacity = capacity });
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
        if (string.IsNullOrWhiteSpace(title) || eventDate == default) return Back("Concert title and date are required.");
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
    public async Task<IActionResult> CreateTicketType(int concertId, string name, decimal price, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name) || price < 0 || capacity <= 0) return Back("Ticket type requires a name, non-negative price, and capacity.");
        _db.TicketTypes.Add(new TicketType { ConcertId = concertId, Name = name.Trim(), Price = price, Capacity = capacity });
        await _db.SaveChangesAsync(); return Back("Ticket type added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string entity, int id)
    {
        object? item = entity switch { "venue" => await _db.Venues.FindAsync(id), "artist" => await _db.Artists.FindAsync(id), "concert" => await _db.Concerts.FindAsync(id), "schedule" => await _db.ConcertSchedules.FindAsync(id), "ticketType" => await _db.TicketTypes.FindAsync(id), "accessory" => await _db.Accessories.FindAsync(id), _ => null };
        if (item is null) return Back("Item not found.");
        _db.Remove(item);
        try { await _db.SaveChangesAsync(); return Back("Item deleted."); }
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
        var accessory = await _db.Accessories.FindAsync(id);
        if (accessory is null) return Back("Merch item not found.");
        accessory.StockQuantity = stockQuantity;
        if (!string.IsNullOrWhiteSpace(imageUrl)) accessory.ImageUrl = imageUrl.Trim();
        await _db.SaveChangesAsync(); return Back("Stock updated.");
    }

    private static bool IsValidUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) || (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateSeats(int concertId, string section, int rowStart, int rowEnd, int seatsPerRow, decimal price, int? ticketTypeId)
    {
        if (string.IsNullOrWhiteSpace(section) || rowStart <= 0 || rowEnd < rowStart || seatsPerRow <= 0 || price < 0)
            return Back("Seat generation requires a section, valid row range, seats per row, and non-negative price.");
        if (!await _db.Concerts.AnyAsync(c => c.Id == concertId)) return Back("Selected concert does not exist.");

        TicketType? ticketType = null;
        if (ticketTypeId is not null)
        {
            ticketType = await _db.TicketTypes.FirstOrDefaultAsync(t => t.Id == ticketTypeId && t.ConcertId == concertId);
            if (ticketType is null) return Back("Selected ticket type does not belong to this concert.");
        }

        var existing = await _db.Seats.Where(s => s.ConcertId == concertId && s.Section == section).Select(s => new { s.Row, s.SeatNumber }).ToListAsync();
        var existingKeys = existing.Select(s => (s.Row, s.SeatNumber)).ToHashSet();

        var seats = new List<Seat>();
        for (var row = rowStart; row <= rowEnd; row++)
        {
            for (var num = 1; num <= seatsPerRow; num++)
            {
                if (existingKeys.Contains((row.ToString(), num))) continue;
                seats.Add(new Seat
                {
                    ConcertId = concertId,
                    Section = section.Trim(),
                    Row = row.ToString(),
                    SeatNumber = num,
                    Price = ticketType?.Price ?? price,
                    TicketTypeId = ticketType?.Id,
                    Status = SeatStatus.Available
                });
            }
        }

        if (seats.Count == 0) return Back("All seats in that range already exist.");
        _db.Seats.AddRange(seats);
        await _db.SaveChangesAsync();
        return Back($"{seats.Count} seat(s) generated.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignSeatTier(int concertId, string section, int? ticketTypeId)
    {
        if (string.IsNullOrWhiteSpace(section)) return Back("Select a section to retier.");

        TicketType? ticketType = null;
        if (ticketTypeId is not null)
        {
            ticketType = await _db.TicketTypes.FirstOrDefaultAsync(t => t.Id == ticketTypeId && t.ConcertId == concertId);
            if (ticketType is null) return Back("Selected ticket type does not belong to this concert.");
        }

        var seats = await _db.Seats.Where(s => s.ConcertId == concertId && s.Section == section).ToListAsync();
        if (seats.Count == 0) return Back("No seats found in that section.");

        foreach (var seat in seats)
        {
            seat.TicketTypeId = ticketType?.Id;
            if (ticketType is not null) seat.Price = ticketType.Price;
        }

        await _db.SaveChangesAsync();
        return Back($"{seats.Count} seat(s) retiered to {(ticketType is null ? "Unassigned" : ticketType.Name)}.");
    }

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
    public List<SeatSectionSummary> SeatSections { get; set; } = new();
    public string? Message { get; set; }
}

public class SeatSectionSummary
{
    public int ConcertId { get; set; }
    public string ConcertTitle { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public int SeatCount { get; set; }
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }
    public string TicketTypeName { get; set; } = string.Empty;
}
