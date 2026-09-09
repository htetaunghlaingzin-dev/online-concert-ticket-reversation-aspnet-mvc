using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineConcertTicketingReservationSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketStockBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_OrderId_SeatId",
                table: "Tickets");

            migrationBuilder.AddColumn<int>(
                name: "AvailableStock",
                table: "TicketTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TicketTypes",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AlterColumn<int>(
                name: "SeatId",
                table: "Tickets",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "TicketTypeId",
                table: "Tickets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TicketTypeName",
                table: "Tickets",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "Tickets",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "StockDeducted",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TicketTypeId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TicketTypeName",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Preserve old rows and amounts. Only unreserved seats become sellable stock.
            // Legacy pending/booked seats stay outside stock until released by their order.
            migrationBuilder.Sql("""
                UPDATE tt SET AvailableStock = CASE
                    WHEN EXISTS (SELECT 1 FROM Seats s WHERE s.TicketTypeId = tt.Id)
                    THEN (SELECT COUNT(*) FROM Seats s WHERE s.TicketTypeId = tt.Id AND s.Status = 0 AND s.CurrentOrderId IS NULL)
                    WHEN tt.Capacity > 0 THEN tt.Capacity ELSE 0 END
                FROM TicketTypes tt;

                UPDATE t SET UnitPrice = s.Price, TicketTypeName = COALESCE(tt.Name, N'Legacy ticket')
                FROM Tickets t JOIN Seats s ON t.SeatId = s.Id
                LEFT JOIN TicketTypes tt ON s.TicketTypeId = tt.Id;

                -- Normalize only a single unambiguous General/Normal tier. Keep all other historical types.
                UPDATE tt SET Name = N'GA' FROM TicketTypes tt
                WHERE tt.Name IN (N'General', N'Normal')
                  AND NOT EXISTS (SELECT 1 FROM TicketTypes x WHERE x.ConcertId = tt.ConcertId AND x.Name = N'GA')
                  AND (SELECT COUNT(*) FROM TicketTypes x WHERE x.ConcertId = tt.ConcertId AND x.Name IN (N'General', N'Normal')) = 1;

                -- Missing tiers start sold out; never invent new capacity or change existing prices.
                INSERT INTO TicketTypes (ConcertId, Name, Price, Capacity, AvailableStock)
                SELECT c.Id, names.Name, 0, 0, 0 FROM Concerts c
                CROSS JOIN (VALUES (N'VIP'), (N'GA'), (N'VVIP')) names(Name)
                WHERE NOT EXISTS (SELECT 1 FROM TicketTypes tt WHERE tt.ConcertId = c.Id AND tt.Name = names.Name);
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TicketTypes_StockAndPrice",
                table: "TicketTypes",
                sql: "[AvailableStock] >= 0 AND [Price] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_OrderId_SeatId",
                table: "Tickets",
                columns: new[] { "OrderId", "SeatId" },
                unique: true,
                filter: "[SeatId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TicketTypeId",
                table: "Tickets",
                column: "TicketTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TicketTypeId",
                table: "Orders",
                column: "TicketTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_TicketTypes_TicketTypeId",
                table: "Orders",
                column: "TicketTypeId",
                principalTable: "TicketTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_TicketTypes_TicketTypeId",
                table: "Tickets",
                column: "TicketTypeId",
                principalTable: "TicketTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM Orders WHERE TicketTypeId IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM Tickets WHERE SeatId IS NULL)
                    THROW 51001, 'Cannot roll back ticket stock booking after ticket orders exist. Preserve records and roll forward.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_TicketTypes_TicketTypeId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_TicketTypes_TicketTypeId",
                table: "Tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TicketTypes_StockAndPrice",
                table: "TicketTypes");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_OrderId_SeatId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_TicketTypeId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TicketTypeId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AvailableStock",
                table: "TicketTypes");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TicketTypes");

            migrationBuilder.DropColumn(
                name: "TicketTypeId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "TicketTypeName",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StockDeducted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TicketTypeId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TicketTypeName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "Orders");

            migrationBuilder.AlterColumn<int>(
                name: "SeatId",
                table: "Tickets",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_OrderId_SeatId",
                table: "Tickets",
                columns: new[] { "OrderId", "SeatId" },
                unique: true);
        }
    }
}
