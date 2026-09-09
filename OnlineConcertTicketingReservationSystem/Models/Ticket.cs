namespace OnlineConcertTicketingReservationSystem.Models;

public class Ticket
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int? SeatId { get; set; }
    public Seat? Seat { get; set; }
    public int? TicketTypeId { get; set; }
    public TicketType? TicketType { get; set; }
    public string? TicketTypeName { get; set; }
    public decimal UnitPrice { get; set; }
    public string DisplayType => TicketTypeName ?? Seat?.TicketType?.Name ?? "Legacy ticket";
    public decimal DisplayPrice => Seat?.Price ?? UnitPrice;
    public string TicketCode { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
}
