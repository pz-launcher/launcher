using PzLauncher.Core.Common;

namespace PzLauncher.Core.Net;

/// <param name="Name">The site's product name, e.g. <c>GitHub</c>.</param>
/// <param name="Elapsed">Until the response headers arrived, or until the failure.</param>
/// <param name="Error"><c>null</c>: reachable (any HTTP status counts).</param>
public sealed record ConnectionTestResult(string Name, Uri Url, TimeSpan Elapsed, Error? Error);
