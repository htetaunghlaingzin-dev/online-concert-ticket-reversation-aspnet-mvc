namespace OnlineConcertTicketingReservationSystem.Models;

public class ConcertSchedule
{
    public int Id { get; set; }
    public int ConcertId { get; set; }
    public Concert Concert { get; set; } = null!;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}
