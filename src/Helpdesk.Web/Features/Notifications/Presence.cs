using System.Collections.Concurrent;

namespace Helpdesk.Web.Features.Notifications;

/// <summary>
/// Who has a page open right now: every open page keeps a line to <see cref="NotificationHub"/>, so a person with
/// at least one line is online. Kept in memory, which is right for the one server the application runs on.
/// </summary>
public sealed class Presence
{
    private readonly ConcurrentDictionary<string, int> _lines = new(StringComparer.Ordinal);

    public void Opened(string userId) => _lines.AddOrUpdate(userId, 1, (_, count) => count + 1);

    /// <summary>Returns true when that was the person's last open page.</summary>
    public bool Closed(string userId)
    {
        while (_lines.TryGetValue(userId, out var count))
        {
            if (count <= 1)
            {
                if (_lines.TryRemove(new KeyValuePair<string, int>(userId, count)))
                {
                    return true;
                }
            }
            else if (_lines.TryUpdate(userId, count - 1, count))
            {
                return false;
            }
        }

        return false;
    }

    public bool IsOnline(string userId) => _lines.ContainsKey(userId);
}
