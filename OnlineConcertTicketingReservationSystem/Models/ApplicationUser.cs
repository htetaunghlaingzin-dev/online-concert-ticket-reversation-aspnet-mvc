using Microsoft.AspNetCore.Identity;

namespace OnlineConcertTicketingReservationSystem.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public string Name { get; set; } = string.Empty;
}
