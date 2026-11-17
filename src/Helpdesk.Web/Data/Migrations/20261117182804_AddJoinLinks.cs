using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helpdesk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJoinLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsJoinOpen",
                table: "Workspaces",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "JoinCode",
                table: "Workspaces",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Workspaces made before links existed get a code of their own, and their link stays off until an admin turns it on.
            migrationBuilder.Sql("UPDATE [Workspaces] SET [JoinCode] = LOWER(REPLACE(CONVERT(char(36), NEWID()), '-', ''));");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_JoinCode",
                table: "Workspaces",
                column: "JoinCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workspaces_JoinCode",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "IsJoinOpen",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "JoinCode",
                table: "Workspaces");
        }
    }
}
