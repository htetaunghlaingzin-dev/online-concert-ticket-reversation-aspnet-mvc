namespace OnlineConcertTicketingReservationSystem.Models;

public class ConcertArtist
{
    public int ConcertId { get; set; }
    public Concert Concert { get; set; } = null!;
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public int PerformanceOrder { get; set; }
}
