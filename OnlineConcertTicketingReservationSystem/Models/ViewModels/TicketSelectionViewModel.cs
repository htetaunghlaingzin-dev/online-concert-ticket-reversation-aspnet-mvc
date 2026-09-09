using System.ComponentModel.DataAnnotations;

namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class TicketSelectionViewModel
{
    public Concert Concert { get; set; } = null!;
    public string? ErrorMessage { get; set; }
}

public class ReserveTicketsRequest
{
    [Range(1, int.MaxValue)] public int ConcertId { get; set; }
    [Range(1, int.MaxValue)] public int TicketTypeId { get; set; }
    [Range(1, 100)] public int Quantity { get; set; }
    public List<AccessorySelection> Accessories { get; set; } = new();
}
