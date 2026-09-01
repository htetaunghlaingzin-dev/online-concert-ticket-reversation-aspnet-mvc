namespace OnlineConcertTicketingReservationSystem.Models;

public class TicketType
{
    public int Id { get; set; }
    public int ConcertId { get; set; }
    public Concert Concert { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Capacity { get; set; }
}
