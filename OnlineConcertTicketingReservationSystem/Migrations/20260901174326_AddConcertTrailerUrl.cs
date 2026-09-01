using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineConcertTicketingReservationSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddConcertTrailerUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrailerUrl",
                table: "Concerts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrailerUrl",
                table: "Concerts");
        }
    }
}
