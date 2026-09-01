using OnlineConcertTicketingReservationSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace OnlineConcertTicketingReservationSystem.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Concert> Concerts => Set<Concert>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Accessory> Accessories => Set<Accessory>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderAccessory> OrderAccessories => Set<OrderAccessory>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<ConcertArtist> ConcertArtists => Set<ConcertArtist>();
    public DbSet<ConcertSchedule> ConcertSchedules => Set<ConcertSchedule>();
    public DbSet<TicketType> TicketTypes => Set<TicketType>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Concert>(entity =>
        {
            entity.Property(c => c.TrailerUrl).HasMaxLength(1000);
        });

        builder.Entity<Seat>(entity =>
        {
            entity.HasOne(s => s.Concert)
                .WithMany(c => c.Seats)
                .HasForeignKey(s => s.ConcertId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(s => new { s.ConcertId, s.Section, s.Row, s.SeatNumber }).IsUnique();
            entity.Property(s => s.Price).HasColumnType("decimal(18,2)");
            entity.Property(s => s.RowVersion).IsRowVersion();

            entity.HasOne(s => s.TicketType)
                .WithMany()
                .HasForeignKey(s => s.TicketTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Accessory>(entity =>
        {
            entity.HasOne(a => a.Concert)
                .WithMany(c => c.Accessories)
                .HasForeignKey(a => a.ConcertId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(a => a.Price).HasColumnType("decimal(18,2)");
            entity.Property(a => a.ImageUrl).HasMaxLength(1000);
        });

        builder.Entity<Order>(entity =>
        {
            entity.HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.Concert)
                .WithMany(c => c.Orders)
                .HasForeignKey(o => o.ConcertId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
            entity.Property(o => o.CancellationReason).HasMaxLength(500);

            entity.HasIndex(o => o.TransactionRefId)
                .IsUnique()
                .HasFilter("[TransactionRefId] IS NOT NULL");
        });

        builder.Entity<OrderAccessory>(entity =>
        {
            entity.HasOne(oa => oa.Order)
                .WithMany(o => o.OrderAccessories)
                .HasForeignKey(oa => oa.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(oa => oa.Accessory)
                .WithMany()
                .HasForeignKey(oa => oa.AccessoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(oa => oa.UnitPrice).HasColumnType("decimal(18,2)");
        });

        builder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasIndex(p => p.Reference).IsUnique();
            entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            entity.HasOne(p => p.Order).WithMany(o => o.PaymentTransactions)
                .HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Ticket>(entity =>
        {
            entity.HasIndex(t => t.TicketCode).IsUnique();
            entity.HasIndex(t => new { t.OrderId, t.SeatId }).IsUnique();
            entity.Property(t => t.RevocationReason).HasMaxLength(500);
            entity.HasOne(t => t.Order).WithMany(o => o.Tickets)
                .HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(t => t.Seat).WithMany()
                .HasForeignKey(t => t.SeatId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ConcertArtist>().HasKey(ca => new { ca.ConcertId, ca.ArtistId });
        builder.Entity<ConcertArtist>().HasOne(ca => ca.Concert).WithMany(c => c.ConcertArtists)
            .HasForeignKey(ca => ca.ConcertId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ConcertArtist>().HasOne(ca => ca.Artist).WithMany(a => a.ConcertArtists)
            .HasForeignKey(ca => ca.ArtistId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ConcertSchedule>().HasOne(s => s.Concert).WithMany(c => c.Schedules)
            .HasForeignKey(s => s.ConcertId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<TicketType>(entity =>
        {
            entity.Property(t => t.Price).HasColumnType("decimal(18,2)");
            entity.HasOne(t => t.Concert).WithMany(c => c.TicketTypes)
                .HasForeignKey(t => t.ConcertId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
