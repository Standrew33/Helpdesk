using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helpdesk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDemoAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The shared demo accounts (@helpdesk.test) are gone from the application. Tickets opened by the demo
            // customer go with everything in them. The accounts themselves are deleted the way the application deletes
            // people: the rows stay, so that what the demo staff wrote on other tickets keeps an author, shown as
            // "Deleted user", and nothing about them can be used again.
            migrationBuilder.Sql("""
                DELETE t FROM [Tickets] t
                JOIN [AspNetUsers] u ON u.[Id] = t.[CustomerId]
                WHERE u.[NormalizedEmail] LIKE N'%@HELPDESK.TEST';

                DELETE r FROM [AspNetUserRoles] r
                JOIN [AspNetUsers] u ON u.[Id] = r.[UserId]
                WHERE u.[NormalizedEmail] LIKE N'%@HELPDESK.TEST';

                DELETE FROM [SignInChallenges] WHERE [Email] LIKE N'%@HELPDESK.TEST';

                UPDATE [AspNetUsers] SET
                    [DisplayName] = N'Deleted user',
                    [UserName] = CONCAT(N'deleted-', [Id], N'@deleted.invalid'),
                    [NormalizedUserName] = UPPER(CONCAT(N'deleted-', [Id], N'@deleted.invalid')),
                    [Email] = CONCAT(N'deleted-', [Id], N'@deleted.invalid'),
                    [NormalizedEmail] = UPPER(CONCAT(N'deleted-', [Id], N'@deleted.invalid')),
                    [EmailConfirmed] = 0,
                    [PasswordHash] = NULL,
                    [PhoneNumber] = NULL,
                    [SecurityStamp] = CONVERT(nvarchar(36), NEWID()),
                    [IsActive] = 0,
                    [DeletedAt] = SYSDATETIMEOFFSET()
                WHERE [NormalizedEmail] LIKE N'%@HELPDESK.TEST';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deleted data cannot come back.
        }
    }
}
