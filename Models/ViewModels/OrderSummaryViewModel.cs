using OnlineConcertTicketingReservationSystem.Models.Enums;

namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class OrderSummaryViewModel
{
    public int Id { get; set; }
    public string ConcertTitle { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}
