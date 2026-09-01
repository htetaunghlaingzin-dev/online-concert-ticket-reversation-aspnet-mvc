using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineConcertTicketingReservationSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketRevocationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "Tickets",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevocationReason",
                table: "Tickets");
        }
    }
}
