namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class CheckoutViewModel
{
    public int OrderId { get; set; }
    public string ConcertTitle { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime? LockExpiresAt { get; set; }
    public int ConcertId { get; set; }
    public bool CanPay { get; set; }
    public List<string> TicketSummaries { get; set; } = new();
    public List<string> AccessorySummaries { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
