namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class ReserveSeatsRequest
{
    public int ConcertId { get; set; }
    public List<int> SeatIds { get; set; } = new();
    public List<AccessorySelection> Accessories { get; set; } = new();
}
