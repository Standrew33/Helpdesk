using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helpdesk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class NumberTicketsPerWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Tickets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "Tickets",
                type: "int",
                nullable: true);

            // Every ticket so far is in the shared workspace and keeps the number it has always shown;
            // the next ticket there continues after the highest one.
            migrationBuilder.Sql("""
                UPDATE [Tickets] SET [WorkspaceId] = 1, [Number] = [Id];
                UPDATE [Workspaces] SET [LastTicketNumber] = ISNULL((SELECT MAX([Id]) FROM [Tickets]), 0) WHERE [Id] = 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "Number",
                table: "Tickets",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "WorkspaceId",
                table: "Tickets",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_WorkspaceId_Number",
                table: "Tickets",
                columns: new[] { "WorkspaceId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Workspaces_WorkspaceId",
                table: "Tickets",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Workspaces_WorkspaceId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_WorkspaceId_Number",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Tickets");
        }
    }
}
