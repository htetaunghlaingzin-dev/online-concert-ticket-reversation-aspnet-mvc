namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class RevokeTicketRequest
{
    public int OrderId { get; set; }
    public int TicketId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
