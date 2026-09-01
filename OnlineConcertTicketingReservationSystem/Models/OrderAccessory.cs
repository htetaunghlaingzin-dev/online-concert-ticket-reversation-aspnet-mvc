namespace OnlineConcertTicketingReservationSystem.Models;

public class OrderAccessory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int AccessoryId { get; set; }
    public Accessory Accessory { get; set; } = null!;

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
