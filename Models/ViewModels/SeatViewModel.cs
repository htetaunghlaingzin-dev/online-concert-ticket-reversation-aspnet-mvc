using OnlineConcertTicketingReservationSystem.Models.Enums;

namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class SeatViewModel
{
    public int Id { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Row { get; set; } = string.Empty;
    public int SeatNumber { get; set; }
    public decimal Price { get; set; }
    public SeatStatus Status { get; set; }
    public string? TicketTypeName { get; set; }
}
