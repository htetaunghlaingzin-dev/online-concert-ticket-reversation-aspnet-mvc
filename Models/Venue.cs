namespace OnlineConcertTicketingReservationSystem.Models;

public class Venue
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public ICollection<Concert> Concerts { get; set; } = new List<Concert>();
}
