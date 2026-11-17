using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Data;

public static class TicketNumbers
{
    /// <summary>
    /// Takes the next ticket number of a workspace. The counter is raised and read in one statement, so two tickets
    /// opened at the same moment never get the same number. Inside a transaction the number is given back when the
    /// transaction is rolled back.
    /// </summary>
    public static async Task<int> ReserveAsync(HelpdeskDbContext db, int workspaceId, CancellationToken ct = default)
    {
        var numbers = await db.Database
            .SqlQuery<int>($"""
                UPDATE [Workspaces] SET [LastTicketNumber] = [LastTicketNumber] + 1
                OUTPUT INSERTED.[LastTicketNumber] AS [Value]
                WHERE [Id] = {workspaceId}
                """)
            .ToListAsync(ct);

        return numbers is [var number]
            ? number
            : throw new InvalidOperationException($"Workspace {workspaceId} does not exist.");
    }
}
