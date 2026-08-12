using System;
using System.Collections.Generic;
using System.Linq;

namespace PropRiskManager.Domain;

public static class SmartAlertHistoryRepository
{
    public static int AppendDistinct(List<SmartAlertEvent> history, IEnumerable<SmartAlertEvent> events)
    {
        var known = new HashSet<string>(history.Select(item => item.EventId), StringComparer.Ordinal);
        var appended = 0;

        foreach (var alertEvent in events)
        {
            if (string.IsNullOrWhiteSpace(alertEvent.EventId) || !known.Add(alertEvent.EventId))
                continue;

            history.Add(alertEvent);
            appended++;
        }

        return appended;
    }

    public static IReadOnlyList<SmartAlertEvent> GetForPosition(IEnumerable<SmartAlertEvent> history, int positionId) =>
        history
            .Where(item => item.PositionId == positionId)
            .OrderByDescending(item => item.TriggeredAtUtc)
            .ToArray();
}
