namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class SeatSelectionViewModel
{
    public int ConcertId { get; set; }
    public string ConcertTitle { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public List<SeatViewModel> Seats { get; set; } = new();
    public List<AccessoryViewModel> Accessories { get; set; } = new();
    public List<TicketTypeLegendItem> TicketTypeLegend { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public List<int> ConflictingSeatIds { get; set; } = new();
}

public class TicketTypeLegendItem
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
