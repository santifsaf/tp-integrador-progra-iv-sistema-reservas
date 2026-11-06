using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Booking.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAmountAndCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "amount_cents",
                table: "reservations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "reservations",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "amount_cents",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "reservations");
        }
    }
}
