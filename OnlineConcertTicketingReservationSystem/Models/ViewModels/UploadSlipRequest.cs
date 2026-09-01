using Microsoft.AspNetCore.Http;

namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class UploadSlipRequest
{
    public int OrderId { get; set; }
    public IFormFile SlipImage { get; set; } = null!;
    public string TransactionRefId { get; set; } = string.Empty;
}
