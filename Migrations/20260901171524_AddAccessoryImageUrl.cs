using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineConcertTicketingReservationSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessoryImageUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Accessories",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Accessories");
        }
    }
}
