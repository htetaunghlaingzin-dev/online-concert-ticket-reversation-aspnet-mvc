namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class VerificationQueueItemViewModel
{
    public int OrderId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string ConcertTitle { get; set; } = string.Empty;
    public List<string> SeatLabels { get; set; } = new();
    public List<string> AccessorySummaries { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public string? PaymentSlipUrl { get; set; }
    public string? TransactionRefId { get; set; }
    public DateTime CreatedAt { get; set; }
}
