using System.ComponentModel.DataAnnotations;
using OnlineConcertTicketingReservationSystem.Models.Enums;

namespace OnlineConcertTicketingReservationSystem.Models;

public class Seat
{
    public int Id { get; set; }
    public int ConcertId { get; set; }
    public Concert Concert { get; set; } = null!;

    public string Section { get; set; } = string.Empty;
    public string Row { get; set; } = string.Empty;
    public int SeatNumber { get; set; }
    public decimal Price { get; set; }
    public SeatStatus Status { get; set; } = SeatStatus.Available;
    public int? CurrentOrderId { get; set; }

    public int? TicketTypeId { get; set; }
    public TicketType? TicketType { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = null!;
}
