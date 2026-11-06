using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Availability.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PriceCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReservationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seats_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Events",
                columns: new[] { "Id", "Currency", "Name", "PriceCents", "StartsAt" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "ARS", "Festival de musica", 15000L, new DateTime(2026, 12, 1, 20, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "ARS", "Conferencia de tecnologia", 20000L, new DateTime(2027, 1, 15, 18, 30, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "Seats",
                columns: new[] { "Id", "EventId", "Label" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111101"), new Guid("11111111-1111-1111-1111-111111111111"), "A1" },
                    { new Guid("11111111-1111-1111-1111-111111111102"), new Guid("11111111-1111-1111-1111-111111111111"), "A2" },
                    { new Guid("11111111-1111-1111-1111-111111111103"), new Guid("11111111-1111-1111-1111-111111111111"), "B1" },
                    { new Guid("11111111-1111-1111-1111-111111111104"), new Guid("11111111-1111-1111-1111-111111111111"), "B2" },
                    { new Guid("22222222-2222-2222-2222-222222222201"), new Guid("22222222-2222-2222-2222-222222222222"), "A1" },
                    { new Guid("22222222-2222-2222-2222-222222222202"), new Guid("22222222-2222-2222-2222-222222222222"), "A2" },
                    { new Guid("22222222-2222-2222-2222-222222222203"), new Guid("22222222-2222-2222-2222-222222222222"), "B1" },
                    { new Guid("22222222-2222-2222-2222-222222222204"), new Guid("22222222-2222-2222-2222-222222222222"), "B2" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationRequests_UserId",
                table: "ReservationRequests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Seats_EventId",
                table: "Seats",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationRequests");

            migrationBuilder.DropTable(
                name: "Seats");

            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
