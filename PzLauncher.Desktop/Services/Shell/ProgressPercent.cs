using System;

namespace PzLauncher.Desktop.Services.Shell;

internal static class ProgressPercent
{
    /// <summary>0 to 100; <c>null</c> while the total is unknown or not positive.</summary>
    public static int? Of(double done, double? total) =>
        total is > 0 and var known ? (int)Math.Clamp(done * 100 / known, 0, 100) : null;
}
