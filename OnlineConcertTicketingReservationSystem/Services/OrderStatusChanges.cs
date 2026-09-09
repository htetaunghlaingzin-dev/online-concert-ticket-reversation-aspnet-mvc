using OnlineConcertTicketingReservationSystem.Models.Enums;

namespace OnlineConcertTicketingReservationSystem.Services;

public static class OrderStatusChanges
{
    public static OrderStatus[] Allowed(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment or OrderStatus.PendingApproval =>
            [OrderStatus.Confirmed, OrderStatus.Rejected, OrderStatus.Cancelled],
        OrderStatus.Confirmed => [OrderStatus.Cancelled],
        _ => []
    };

    public static string Label(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "Pending payment",
        OrderStatus.PendingApproval => "Pending approval",
        _ => status.ToString()
    };
}
