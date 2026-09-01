namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class VerifyOrderRequest
{
    public int OrderId { get; set; }
    public bool IsApproved { get; set; }
    public string? RejectionReason { get; set; }
}
