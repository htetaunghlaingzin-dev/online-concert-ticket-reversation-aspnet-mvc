using Microsoft.AspNetCore.SignalR;

namespace OnlineConcertTicketingReservationSystem.Hubs;

public class SeatHub : Hub
{
    public Task JoinConcertGroup(int concertId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(concertId));

    public Task LeaveConcertGroup(int concertId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(concertId));

    public static string GroupName(int concertId) => $"concert-{concertId}";
}
