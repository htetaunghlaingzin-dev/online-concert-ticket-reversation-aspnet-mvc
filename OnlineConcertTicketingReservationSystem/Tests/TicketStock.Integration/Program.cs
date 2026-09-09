using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OnlineConcertTicketingReservationSystem.Controllers;
using OnlineConcertTicketingReservationSystem.Data;
using OnlineConcertTicketingReservationSystem.Models;
using OnlineConcertTicketingReservationSystem.Models.Enums;
using OnlineConcertTicketingReservationSystem.Models.ViewModels;
using OnlineConcertTicketingReservationSystem.Services;

// No external test framework: exercises the real SQL Server provider and controllers.
// Always creates a new, uniquely named database. Never uses the application's database.
var database = "ConcertStockTest_" + Guid.NewGuid().ToString("N");
var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;Encrypt=False";
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
var services = new ServiceCollection().AddLogging().AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
services.AddIdentity<ApplicationUser, IdentityRole<Guid>>().AddEntityFrameworkStores<ApplicationDbContext>();
services.AddSignalR();
services.AddScoped<SeatNotifier>();
using var provider = services.BuildServiceProvider();
var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var environment = new TestEnvironment();
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
T Setup<T>(T controller, Guid? user = null) where T : Controller
{
    var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, (user ?? userId).ToString()), new Claim(ClaimTypes.Role, "Admin") }, "test")) };
    controller.ControllerContext = new ControllerContext { HttpContext = context };
    controller.TempData = new TempDataDictionary(context, new MemoryTempData());
    return controller;
}
BookingController Booking(IServiceScope scope) => Setup(new BookingController(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
    scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), scope.ServiceProvider.GetRequiredService<SeatNotifier>(), environment));
AdminController Admin(IServiceScope scope) => Setup(new AdminController(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), scope.ServiceProvider.GetRequiredService<SeatNotifier>()));
MockPaymentController Payment(IServiceScope scope) => Setup(new MockPaymentController(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
    scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), scope.ServiceProvider.GetRequiredService<SeatNotifier>()));
async Task<int> Reserve(int typeId, int quantity = 1)
{
    using var scope = provider.CreateScope();
    var result = await Booking(scope).ReserveTickets(new ReserveTicketsRequest { ConcertId = 1, TicketTypeId = typeId, Quantity = quantity });
    if (result is not RedirectToActionResult { ActionName: "Checkout" } redirect) throw new Exception("Reservation failed");
    return (int)redirect.RouteValues!["orderId"]!;
}
async Task<int> StartPayment(int orderId)
{
    using var scope = provider.CreateScope();
    var result = (ViewResult)await Payment(scope).Pay(orderId);
    return ((PaymentTransaction)result.Model!).Id;
}
async Task Complete(int paymentId)
{
    using var scope = provider.CreateScope();
    await Payment(scope).Complete(paymentId, true);
}
async Task<int> Stock(int id)
{
    await using var db = new ApplicationDbContext(options);
    return await db.TicketTypes.Where(t => t.Id == id).Select(t => t.AvailableStock).SingleAsync();
}
try
{
    await using (var db = new ApplicationDbContext(options))
    {
        await db.GetService<IMigrator>().MigrateAsync("20260901174326_AddConcertTrailerUrl");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT AspNetUsers (Id,Name,UserName,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount)
            VALUES ('11111111-1111-1111-1111-111111111111',N'Legacy customer',N'legacy',0,0,0,0,0);
            INSERT Concerts (Title,VenueType,EventDate) VALUES (N'Migration concert',0,DATEADD(day,30,GETUTCDATE()));
            INSERT TicketTypes (ConcertId,Name,Price,Capacity) VALUES (1,N'VIP',150,50),(1,N'General',90,8);
            INSERT Orders (UserId,ConcertId,TotalAmount,Status,LockExpiresAt,CreatedAt)
            VALUES ('11111111-1111-1111-1111-111111111111',1,150,0,DATEADD(day,1,GETUTCDATE()),GETUTCDATE()),
                   ('11111111-1111-1111-1111-111111111111',1,150,2,NULL,GETUTCDATE());
            INSERT Seats (ConcertId,Section,Row,SeatNumber,Price,Status,CurrentOrderId,TicketTypeId)
            VALUES (1,N'A',N'1',1,150,0,NULL,1),(1,N'A',N'1',2,150,1,1,1),(1,N'A',N'1',3,150,2,2,1);
            INSERT Tickets (OrderId,SeatId,TicketCode,IssuedAt) VALUES (2,3,N'LEGACY-CODE',GETUTCDATE());
            """);
        await db.Database.MigrateAsync();
        Check(!db.Database.HasPendingModelChanges(), "migration and model snapshot agree");
        Check(await db.Seats.CountAsync() == 3 && await db.Users.CountAsync() == 1 && await db.Orders.SumAsync(o => o.TotalAmount) == 300m, "migration preserves seats, users, orders and numeric amounts");
        Check(await db.TicketTypes.Where(t => t.Id == 1).Select(t => t.AvailableStock).SingleAsync() == 1, "migration excludes booked and reserved seats from available stock");
        Check(await db.TicketTypes.AnyAsync(t => t.Id == 2 && t.Name == "GA" && t.Price == 90 && t.AvailableStock == 8), "General becomes GA with unchanged price and configured stock");
        Check(await db.TicketTypes.AnyAsync(t => t.Name == "VVIP" && t.AvailableStock == 0), "missing tier starts sold out");
        Check((await db.Tickets.SingleAsync()).TicketCode == "LEGACY-CODE", "legacy QR ticket is preserved");
    }

    using (var scope = provider.CreateScope())
    {
        var management = Setup(new ManagementController(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()));
        await management.CreateTicketType(1, "VIP", 1, 100);
        await management.CreateTicketType(1, "Invalid", 1, 100);
        await management.CreateTicketType(999999, "VIP", 1, 100);
        Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TicketTypes.CountAsync() == 3, "admin rejects duplicate, unknown and cross-concert ticket types");
        var type = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TicketTypes.SingleAsync(t => t.Id == 2);
        var version = Convert.ToBase64String(type.RowVersion);
        await management.UpdateTicketType(2, 95m, 8, version);
        Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TicketTypes.AnyAsync(t => t.Id == 2 && t.Price == 95m), "admin updates MMK price and stock");
    }
    using (var scope = provider.CreateScope())
    {
        var booking = Booking(scope);
        Check((await booking.ReserveTickets(new ReserveTicketsRequest { ConcertId = 1, TicketTypeId = 1, Quantity = 2 })) is RedirectToActionResult { ActionName: "SelectTickets" }, "checkout rejects quantities above available stock");
        Check((await booking.ReserveTickets(new ReserveTicketsRequest { ConcertId = 2, TicketTypeId = 1, Quantity = 1 })) is RedirectToActionResult { ActionName: "SelectTickets" }, "checkout rejects ticket type from another concert");
        booking.ModelState.AddModelError("Quantity", "Out of range");
        Check((await booking.ReserveTickets(new ReserveTicketsRequest { ConcertId = 1, TicketTypeId = 1, Quantity = 0 })) is RedirectToActionResult { ActionName: "SelectTickets" }, "invalid quantity model state is rejected");
    }

    string staleVersion;
    await using (var db = new ApplicationDbContext(options)) staleVersion = Convert.ToBase64String((await db.TicketTypes.SingleAsync(t => t.Id == 1)).RowVersion);
    var first = await Reserve(1);
    var second = await Reserve(1);
    Check(await Stock(1) == 1, "creating pending orders does not deduct stock");
    var firstPayment = await StartPayment(first);
    var secondPayment = await StartPayment(second);
    await Task.WhenAll(Complete(firstPayment), Complete(secondPayment));
    int winner;
    await using (var db = new ApplicationDbContext(options))
    {
        var orders = await db.Orders.Where(o => o.Id == first || o.Id == second).ToListAsync();
        Check(orders.Count(o => o.Status == OrderStatus.Confirmed) == 1 && await Stock(1) == 0, "simultaneous customers cannot oversell the last ticket");
        Check(await db.PaymentTransactions.CountAsync(p => p.Status == PaymentStatus.Succeeded) == 1, "only successful inventory confirmation records a successful payment");
        winner = orders.Single(o => o.Status == OrderStatus.Confirmed).Id;
        Check(await db.Tickets.CountAsync(t => t.OrderId == winner && t.SeatId == null) == 1, "confirmation issues a seatless QR ticket");
    }
    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Complete(winner == first ? firstPayment : secondPayment)));
    await using (var db = new ApplicationDbContext(options))
        Check(await Stock(1) == 0 && await db.Tickets.CountAsync(t => t.OrderId == winner) == 1, "eight duplicate callbacks do not deduct stock or issue duplicate tickets");
    using (var scope = provider.CreateScope())
    {
        var management = Setup(new ManagementController(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()));
        await management.UpdateTicketType(1, 150, 100, staleVersion);
        Check(await Stock(1) == 0, "stale admin form cannot overwrite stock after a purchase");
        var details = (PartialViewResult)await Admin(scope).OrderDetail(winner, true);
        var model = (OrderDetailViewModel)details.Model!;
        Check(model.TicketSummary.Single().Name == "VIP" && model.TicketSummary.Single().Quantity == 1 && model.TicketSummary.Single().UnitPrice == 150 && model.PaymentStatus == "Paid", "order popup contains saved ticket type, quantity, MMK price and payment status");
        var unauthorized = Setup(Booking(scope), Guid.NewGuid());
        Check(await unauthorized.Checkout(winner) is NotFoundResult, "checkout ownership is enforced");
    }
    await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => {
        using var scope = provider.CreateScope(); await Admin(scope).CancelOrder(new CancelOrderRequest { OrderId = winner, Reason = "Integration test" });
    }));
    Check(await Stock(1) == 1, "concurrent cancellation restores stock exactly once");

    var quantityOrder = await Reserve(2, 3);
    using (var scope = provider.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var type = await db.TicketTypes.SingleAsync(t => t.Id == 2);
        await Setup(new ManagementController(db)).UpdateTicketType(2, 120, 8, Convert.ToBase64String(type.RowVersion));
        var checkout = (ViewResult)await Booking(scope).Checkout(quantityOrder);
        Check(((CheckoutViewModel)checkout.Model!).TotalAmount == 285m, "checkout keeps the original price after an admin price change");
    }
    await Complete(await StartPayment(quantityOrder));
    await using (var db = new ApplicationDbContext(options))
        Check(await Stock(2) == 5 && await db.Tickets.CountAsync(t => t.OrderId == quantityOrder && t.UnitPrice == 95m) == 3, "quantity purchase deducts three tickets and preserves unit price");

    var approval = await Reserve(2, 2);
    await using (var db = new ApplicationDbContext(options))
    {
        var order = await db.Orders.FindAsync(approval); order!.Status = OrderStatus.PendingApproval;
        order.TransactionRefId = "TEST-SLIP"; await db.SaveChangesAsync();
    }
    await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ => {
        using var scope = provider.CreateScope(); await Admin(scope).VerifyOrder(new VerifyOrderRequest { OrderId = approval, IsApproved = true });
    }));
    await using (var db = new ApplicationDbContext(options))
        Check(await Stock(2) == 3 && await db.Tickets.CountAsync(t => t.OrderId == approval) == 2 && await db.PaymentTransactions.CountAsync(p => p.OrderId == approval && p.Status == PaymentStatus.Succeeded) == 1, "manual approval shares atomic, idempotent payment and issuance logic");

    var declined = await Reserve(2);
    var declinedPayment = await StartPayment(declined);
    using (var scope = provider.CreateScope()) await Payment(scope).Complete(declinedPayment, false);
    Check(await Stock(2) == 3, "declined payment leaves stock unchanged");
    await Complete(await StartPayment(declined));
    Check(await Stock(2) == 2, "declined payment can be retried successfully");

    // An outstanding old reservation is excluded from stock until explicitly released.
    await Complete(await StartPayment(1));
    await using (var db = new ApplicationDbContext(options))
        Check(await Stock(1) == 1 && await db.Tickets.CountAsync(t => t.OrderId == 1 && t.SeatId != null) == 1, "legacy payment confirms its existing reservation without a second stock deduction");
    using (var scope = provider.CreateScope())
    {
        var detail = (ViewResult)await Admin(scope).OrderDetail(2);
        Check(((OrderDetailViewModel)detail.Model!).TicketSummary.Single().UnitPrice == 150, "legacy order details render safely");
        await Admin(scope).CancelOrder(new CancelOrderRequest { OrderId = 1, Reason = "Legacy cancellation" });
    }
    Check(await Stock(1) == 2, "legacy cancellation returns its previously excluded stock");

    int ticketId;
    await using (var db = new ApplicationDbContext(options)) ticketId = await db.Tickets.Where(t => t.OrderId == quantityOrder).Select(t => t.Id).FirstAsync();
    await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ => {
        using var scope = provider.CreateScope(); await Admin(scope).RevokeTicket(new RevokeTicketRequest { OrderId = quantityOrder, TicketId = ticketId, Reason = "Test revoke" });
    }));
    Check(await Stock(2) == 3, "repeated single-ticket revocation restores one ticket only");
    using (var scope = provider.CreateScope()) await Admin(scope).CancelOrder(new CancelOrderRequest { OrderId = quantityOrder, Reason = "Remaining tickets" });
    Check(await Stock(2) == 5, "cancellation after a revocation restores only remaining tickets");

    using (var scope = provider.CreateScope())
    {
        var booking = Booking(scope);
        Check(await booking.ReserveTickets(new ReserveTicketsRequest { ConcertId = 1, TicketTypeId = 2, Quantity = -1 }) is RedirectToActionResult { ActionName: "SelectTickets" }, "negative quantities are rejected without relying on browser validation");
    }
    // Exercise actual slip upload transition without reserving inventory prematurely.
    var slipOrder = await Reserve(2, 2);
    using (var scope = provider.CreateScope())
    {
        Directory.CreateDirectory(environment.WebRootPath);
        using var file = new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jfS8AAAAASUVORK5CYII="));
        var result = await Booking(scope).UploadSlip(new UploadSlipRequest { OrderId = slipOrder, TransactionRefId = "UPLOAD-TEST", SlipImage = new FormFile(file, 0, file.Length, "SlipImage", "test.png") });
        Check(result is RedirectToActionResult { ActionName: "Confirmation" } && await Stock(2) == 5, "slip upload moves to verification without deducting stock");
    }
    await using (var db = new ApplicationDbContext(options))
    {
        Check(await db.Orders.AnyAsync(o => o.Id == slipOrder && o.Status == OrderStatus.PendingApproval && o.PaymentSlipUrl != null), "payment slip and reference are saved");
        await db.TicketTypes.Where(t => t.Id == 2).ExecuteUpdateAsync(set => set.SetProperty(t => t.AvailableStock, 1));
    }
    using (var scope = provider.CreateScope()) await Admin(scope).VerifyOrder(new VerifyOrderRequest { OrderId = slipOrder, IsApproved = true });
    await using (var db = new ApplicationDbContext(options))
        Check(await Stock(2) == 1 && !await db.Tickets.AnyAsync(t => t.OrderId == slipOrder) && await db.Orders.AnyAsync(o => o.Id == slipOrder && o.Status == OrderStatus.PendingApproval), "manual approval fails safely if stock was consumed after slip submission");
    using (var scope = provider.CreateScope()) await Admin(scope).VerifyOrder(new VerifyOrderRequest { OrderId = slipOrder, IsApproved = false, RejectionReason = "Stock unavailable" });
    Check(await Stock(2) == 1, "rejecting a new ticket order does not add unreserved stock");

    var rollbackOrder = await Reserve(2);
    await using (var db = new ApplicationDbContext(options))
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(db);
        Check(await TicketInventory.ConfirmAsync(db, (await db.Orders.FindAsync(rollbackOrder))!) == null, "confirmation can be staged inside a payment transaction");
        await db.SaveChangesAsync();
        await tx.RollbackAsync();
    }
    await using (var db = new ApplicationDbContext(options))
        Check(await Stock(2) == 1 && !await db.Tickets.AnyAsync(t => t.OrderId == rollbackOrder) && await db.Orders.AnyAsync(o => o.Id == rollbackOrder && !o.StockDeducted), "transaction rollback restores stock, order state and tickets together");

    await using (var db = new ApplicationDbContext(options))
    {
        var order = new Order { UserId = userId, ConcertId = 1, TotalAmount = 150m, LockExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
        db.Orders.Add(order); await db.SaveChangesAsync();
        var seat = await db.Seats.SingleAsync(s => s.Id == 1); seat.Status = SeatStatus.PendingPayment; seat.CurrentOrderId = order.Id;
        await db.TicketTypes.Where(t => t.Id == 1).ExecuteUpdateAsync(set => set.SetProperty(t => t.AvailableStock, t => t.AvailableStock - 1));
        await db.SaveChangesAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await TicketInventory.LockAsync(db);
        await TicketInventory.ReleaseLegacySeatsAsync(db, order.Id); await db.SaveChangesAsync();
        await TicketInventory.ReleaseLegacySeatsAsync(db, order.Id); await db.SaveChangesAsync();
        await tx.CommitAsync();
        Check(await Stock(1) == 2, "legacy expiration or rejection releases held stock exactly once");
    }

    using (var scope = provider.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var code = await db.Tickets.Where(t => t.OrderId == approval).Select(t => t.TicketCode).FirstAsync();
        var validator = Setup(new TicketController(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()));
        await validator.Validate(code);
        Check(((string)validator.ViewBag.Result).StartsWith("Valid ticket:"), "new seatless QR ticket validates");
    }
    using (var scope = provider.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var code = await db.Tickets.Where(t => t.OrderId == approval && t.ValidatedAt != null).Select(t => t.TicketCode).FirstAsync();
        var validator = Setup(new TicketController(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()));
        await validator.Validate(code);
        Check(((string)validator.ViewBag.Result).StartsWith("Already used"), "used QR ticket cannot be validated again");
    }

    Console.WriteLine($"SUCCESS: {passed} integration checks passed.");
    if (args.Contains("--keep")) Console.WriteLine("Retained isolated UI test database: " + database);
}
finally
{
    if (!args.Contains("--keep"))
    {
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureDeletedAsync();
    }
}

sealed class MemoryTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "TicketStock.Integration";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "concert-stock-test-uploads");
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
