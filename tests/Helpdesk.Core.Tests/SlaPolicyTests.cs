using Helpdesk.Core.Tickets;

namespace Helpdesk.Core.Tests;

public class SlaPolicyTests
{
    [Theory]
    [InlineData(TicketPriority.Urgent, 4)]
    [InlineData(TicketPriority.High, 8)]
    [InlineData(TicketPriority.Normal, 24)]
    [InlineData(TicketPriority.Low, 72)]
    public void ResolutionTime_depends_on_priority(TicketPriority priority, int hours)
    {
        Assert.Equal(TimeSpan.FromHours(hours), SlaPolicy.ResolutionTime(priority));
    }

    [Fact]
    public void CalculateDueAt_adds_resolution_time_to_creation_time()
    {
        var created = new DateTimeOffset(2026, 3, 2, 9, 30, 0, TimeSpan.Zero);

        var due = SlaPolicy.CalculateDueAt(TicketPriority.High, created);

        Assert.Equal(created.AddHours(8), due);
    }

    [Fact]
    public void ResolutionTime_rejects_unknown_priority()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SlaPolicy.ResolutionTime((TicketPriority)99));
    }
}
