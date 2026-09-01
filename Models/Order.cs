using OnlineConcertTicketingReservationSystem.Models.Enums;

namespace OnlineConcertTicketingReservationSystem.Models;

public class Order
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int ConcertId { get; set; }
    public Concert Concert { get; set; } = null!;

    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public DateTime? LockExpiresAt { get; set; }
    public string? PaymentSlipUrl { get; set; }
    public string? TransactionRefId { get; set; }
    public string? RejectionReason { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderAccessory> OrderAccessories { get; set; } = new List<OrderAccessory>();
    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
