using OnlineConcertTicketingReservationSystem.Models.Enums;

namespace OnlineConcertTicketingReservationSystem.Models;

public class Concert
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public VenueType VenueType { get; set; }
    public DateTime EventDate { get; set; }
    public int? VenueId { get; set; }
    public Venue? Venue { get; set; }
    public string? TrailerUrl { get; set; }

    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
    public ICollection<Accessory> Accessories { get; set; } = new List<Accessory>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<ConcertArtist> ConcertArtists { get; set; } = new List<ConcertArtist>();
    public ICollection<ConcertSchedule> Schedules { get; set; } = new List<ConcertSchedule>();
    public ICollection<TicketType> TicketTypes { get; set; } = new List<TicketType>();
}
