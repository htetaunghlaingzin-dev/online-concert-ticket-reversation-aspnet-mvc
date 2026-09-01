namespace OnlineConcertTicketingReservationSystem.Models.ViewModels;

public class AdminDashboardViewModel
{
    public decimal TotalRevenue { get; set; }
    public int TicketsSold { get; set; }
    public int PendingApprovalCount { get; set; }
    public int UpcomingConcertsCount { get; set; }
    public List<LowStockAccessoryViewModel> LowStockAccessories { get; set; } = new();
    public List<OrderSummaryViewModel> RecentOrders { get; set; } = new();
}

public class LowStockAccessoryViewModel
{
    public int Id { get; set; }
    public string ConcertTitle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
}
