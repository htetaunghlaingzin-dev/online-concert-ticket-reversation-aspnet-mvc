using OnlineConcertTicketingReservationSystem.Models;
using OnlineConcertTicketingReservationSystem.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace OnlineConcertTicketingReservationSystem.Data;

public static class DbSeeder
{
    public const string AdminRole = "Admin";
    public const string UserRole = "User";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = services.GetRequiredService<IConfiguration>();

        foreach (var role in new[] { UserRole, AdminRole })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        var adminEmail = configuration["AdminSeed:Email"] ?? "admin@concertticketing.local";
        var adminPassword = configuration["AdminSeed:Password"] ?? "Admin_P@ssw0rd123";
        var adminName = configuration["AdminSeed:Name"] ?? "System Admin";

        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser is null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                Name = adminName,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, AdminRole);
            }
        }
        else if (!await userManager.IsInRoleAsync(adminUser, AdminRole))
        {
            await userManager.AddToRoleAsync(adminUser, AdminRole);
        }

        if (!await db.Concerts.AnyAsync())
        {
            var concerts = new[]
            {
                new Concert
                {
                    Title = "Sample Live Concert",
                    VenueType = VenueType.Indoor,
                    EventDate = DateTime.UtcNow.AddDays(30),
                    TrailerUrl = "https://storage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"
                },
                new Concert
                {
                    Title = "Neon Nights Festival",
                    VenueType = VenueType.Outdoor,
                    EventDate = DateTime.UtcNow.AddDays(45)
                },
                new Concert
                {
                    Title = "Acoustic Sessions",
                    VenueType = VenueType.Indoor,
                    EventDate = DateTime.UtcNow.AddDays(60)
                }
            };
            db.Concerts.AddRange(concerts);
            await db.SaveChangesAsync();

            foreach (var concert in concerts)
            {
                await SeedSeatsAndTiersAsync(db, concert);
            }

            db.Accessories.AddRange(
                new Accessory { ConcertId = concerts[0].Id, Name = "Tour T-Shirt", Price = 25m, StockQuantity = 100, ImageUrl = "https://picsum.photos/seed/tour-tshirt/300/300" },
                new Accessory { ConcertId = concerts[0].Id, Name = "Poster", Price = 10m, StockQuantity = 200, ImageUrl = "https://picsum.photos/seed/tour-poster/300/300" },
                new Accessory { ConcertId = concerts[1].Id, Name = "Tour Hoodie", Price = 45m, StockQuantity = 60, ImageUrl = "https://picsum.photos/seed/tour-hoodie/300/300" },
                new Accessory { ConcertId = concerts[2].Id, Name = "Vinyl Record", Price = 35m, StockQuantity = 50, ImageUrl = "https://picsum.photos/seed/tour-vinyl/300/300" }
            );

            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedSeatsAndTiersAsync(ApplicationDbContext db, Concert concert)
    {
        const int seatsPerRow = 12;
        var vipType = new TicketType { ConcertId = concert.Id, Name = "VIP", Price = 150m, Capacity = 3 * seatsPerRow };
        var generalType = new TicketType { ConcertId = concert.Id, Name = "General", Price = 90m, Capacity = 3 * seatsPerRow };
        db.TicketTypes.AddRange(vipType, generalType);
        await db.SaveChangesAsync();

        var sectionTiers = new Dictionary<string, TicketType> { ["A"] = vipType, ["B"] = generalType };

        var seats = new List<Seat>();
        foreach (var section in new[] { "A", "B" })
        {
            var tier = sectionTiers[section];
            for (var row = 1; row <= 3; row++)
            {
                for (var num = 1; num <= seatsPerRow; num++)
                {
                    seats.Add(new Seat
                    {
                        ConcertId = concert.Id,
                        Section = section,
                        Row = row.ToString(),
                        SeatNumber = num,
                        Price = tier.Price,
                        TicketTypeId = tier.Id,
                        Status = SeatStatus.Available
                    });
                }
            }
        }
        db.Seats.AddRange(seats);
        await db.SaveChangesAsync();
    }
}
