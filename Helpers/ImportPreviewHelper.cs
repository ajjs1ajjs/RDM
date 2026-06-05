using RemoteManager.Services;

namespace RemoteManager.Helpers;

public static class ImportPreviewHelper
{
    public static string BuildPreviewText(ImportPreview preview)
    {
        var groupsPreview = preview.Groups.Count <= 8
            ? string.Join("\n", preview.Groups)
            : string.Join("\n", preview.Groups.Take(8)) + $"\n... (and {preview.Groups.Count - 8} more)";

        var connsPreview = preview.Connections.Count <= 12
            ? string.Join("\n", preview.Connections)
            : string.Join("\n", preview.Connections.Take(12)) + $"\n... (and {preview.Connections.Count - 12} more)";

        return $"Found {preview.GroupCount} groups and {preview.ConnectionCount} connections.\n\n" +
               $"Groups Preview:\n{groupsPreview}\n\n" +
               $"Connections Preview:\n{connsPreview}";
    }

    public static (string GroupsText, string ConnectionsText) BuildPreviewParts(ImportPreview preview)
    {
        var groupsPreview = preview.Groups.Count <= 8
            ? string.Join("\n", preview.Groups)
            : string.Join("\n", preview.Groups.Take(8)) + $"\n... (and {preview.Groups.Count - 8} more)";

        var connsPreview = preview.Connections.Count <= 12
            ? string.Join("\n", preview.Connections)
            : string.Join("\n", preview.Connections.Take(12)) + $"\n... (and {preview.Connections.Count - 12} more)";

        return (groupsPreview, connsPreview);
    }
}
