namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class CancelOrderRequest
{
    public int OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
