using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helpdesk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MarkClosingReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsClosingReason",
                table: "Comments",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsClosingReason",
                table: "Comments");
        }
    }
}
