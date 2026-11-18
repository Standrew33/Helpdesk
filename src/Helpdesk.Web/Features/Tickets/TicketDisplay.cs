using System.Globalization;
using Helpdesk.Core.Tickets;
using Microsoft.AspNetCore.Html;

namespace Helpdesk.Web.Features.Tickets;

public static class TicketDisplay
{
    public static string StatusText(TicketStatus status) => status switch
    {
        TicketStatus.New => "New",
        TicketStatus.InProgress => "In progress",
        TicketStatus.WaitingForCustomer => "Waiting for customer",
        TicketStatus.Closed => "Closed",
        _ => status.ToString(),
    };

    public static string StatusClass(TicketStatus status) => status switch
    {
        TicketStatus.New => "pill pill-new",
        TicketStatus.InProgress => "pill pill-progress",
        TicketStatus.WaitingForCustomer => "pill pill-waiting",
        TicketStatus.Closed => "pill pill-closed",
        _ => "pill",
    };

    public static string PriorityClass(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => "prio prio-urgent",
        TicketPriority.High => "prio prio-high",
        TicketPriority.Normal => "prio prio-normal",
        TicketPriority.Low => "prio prio-low",
        TicketPriority.Backlog => "prio prio-backlog",
        _ => "prio",
    };

    /// <summary>Lists and pickers show the most pressing priority first.</summary>
    public static readonly IReadOnlyList<TicketPriority> PrioritiesFromHighest = Enum.GetValues<TicketPriority>().Reverse().ToArray();

    /// <summary>The priorities a user may choose: customers cannot make a ticket urgent, but keep it urgent if it is.</summary>
    public static IReadOnlyList<TicketPriority> PrioritiesFor(bool isStaff, TicketPriority? current = null)
        => PrioritiesFromHighest.Where(p => isStaff || p != TicketPriority.Urgent || p == current).ToList();

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
    };

    /// <summary>
    /// A moment as "15 Nov 2026, 14:30 UTC"; local-time.js turns it into the reader's own time zone.
    /// </summary>
    public static IHtmlContent Date(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        var iso = utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var text = utc.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " UTC";
        return new HtmlString($"<time datetime=\"{iso}\" data-local-time>{text}</time>");
    }

    /// <summary>A day as "18 Nov", with the year when it is not this year; a dash when there is none.</summary>
    public static string Day(DateOnly? value, DateOnly today) => value switch
    {
        null => "–",
        { } day when day.Year == today.Year => day.ToString("d MMM", CultureInfo.InvariantCulture),
        { } day => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
    };

    /// <summary>A day kept in the history ("2026-03-09") as "9 Mar 2026"; "none" when there was none.</summary>
    public static string DayInHistory(string? iso)
        => DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : "none";

    /// <summary>A day as the value of a date field.</summary>
    public static string DayValue(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>
/// A one-choice priority picker: the form field name, the options offered and the one chosen. A picker inside a
/// scrolling table floats its list above the page (<paramref name="Floating"/>), so the table does not cut it off.
/// </summary>
public sealed record PriorityChoice(string Name, string Id, IReadOnlyList<TicketPriority> Options, TicketPriority Selected, bool Floating = false);

public sealed record FilterOption(string Value, string Text, string CssClass, bool Checked);

/// <summary>A filter list with check boxes: the query name, the text shown while nothing is chosen, and the options.</summary>
public sealed record FilterChoice(string Name, string Placeholder, IReadOnlyList<FilterOption> Options);
