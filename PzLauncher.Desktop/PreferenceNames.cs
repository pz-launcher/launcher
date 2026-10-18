using System;

namespace PzLauncher.Desktop;

public static class PreferenceNames
{
    /// <summary>The member whose persisted name is <paramref name="name"/>, ignoring case.</summary>
    public static T? Parse<T>(string? name, Func<T, string> nameOf)
        where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>())
        {
            if (string.Equals(nameOf(value), name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }
}
