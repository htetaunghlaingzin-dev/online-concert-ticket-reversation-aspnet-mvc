using OnlineConcertTicketingReservationSystem.Models.Enums;

namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class OrderDetailViewModel
{
    public int Id { get; set; }
    public string ConcertTitle { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LockExpiresAt { get; set; }
    public string? TransactionRefId { get; set; }
    public string? RejectionReason { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public List<TicketLineViewModel> Tickets { get; set; } = new();
    public List<string> AccessorySummaries { get; set; } = new();
}

public class TicketLineViewModel
{
    public int Id { get; set; }
    public string SeatLabel { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool Revoked { get; set; }
    public string? RevocationReason { get; set; }
}
