namespace OnlineConcertTicketingReservationSystem.Models;

public class Artist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public ICollection<ConcertArtist> ConcertArtists { get; set; } = new List<ConcertArtist>();
}
