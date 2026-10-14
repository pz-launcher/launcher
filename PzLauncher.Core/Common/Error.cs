using System.Collections.ObjectModel;

namespace PzLauncher.Core.Common;

/// <summary><see cref="Message"/> is for developers and logs; build user text from <see cref="Code"/>.</summary>
public sealed record Error(ErrorCode Code, string Message, ErrorKind Kind)
{
    // JSON source gen assigns null to an init-only property missing from the JSON ("details": null is rejected).
    public IReadOnlyDictionary<string, string> Details
    {
        get;
        init => field = value ?? ReadOnlyDictionary<string, string>.Empty;
    } = ReadOnlyDictionary<string, string>.Empty;
}
