using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helpdesk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class GiveTicketsAChosenDueDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old deadlines were worked out from the priority, never chosen by anyone, so none of them is kept.
            migrationBuilder.DropIndex(
                name: "IX_Tickets_Status_DueAt",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "Tickets");

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "Tickets",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Status_DueDate",
                table: "Tickets",
                columns: new[] { "Status", "DueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_Status_DueDate",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Tickets");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DueAt",
                table: "Tickets",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Status_DueAt",
                table: "Tickets",
                columns: new[] { "Status", "DueAt" });
        }
    }
}
