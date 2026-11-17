using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helpdesk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceBlocking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Workspaces",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "Workspaces");
        }
    }
}
