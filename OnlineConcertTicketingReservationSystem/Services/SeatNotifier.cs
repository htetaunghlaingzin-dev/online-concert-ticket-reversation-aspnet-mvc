using OnlineConcertTicketingReservationSystem.Hubs;
using OnlineConcertTicketingReservationSystem.Models.Enums;
using Microsoft.AspNetCore.SignalR;

namespace OnlineConcertTicketingReservationSystem.Services;

public class SeatNotifier
{
    private readonly IHubContext<SeatHub> _hubContext;

    public SeatNotifier(IHubContext<SeatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifySeatStatusAsync(int concertId, int seatId, SeatStatus status) =>
        _hubContext.Clients.Group(SeatHub.GroupName(concertId))
            .SendAsync("SeatStatusChanged", new { seatId, status = status.ToString() });

    public async Task NotifySeatsStatusAsync(int concertId, IEnumerable<int> seatIds, SeatStatus status)
    {
        foreach (var seatId in seatIds)
        {
            await NotifySeatStatusAsync(concertId, seatId, status);
        }
    }
}
