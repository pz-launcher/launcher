namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>Records reports synchronously, unlike <see cref="Progress{T}"/>, which posts them.</summary>
public sealed class ListProgress<T>(Action<T>? onReport = null) : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value)
    {
        Reports.Add(value);
        onReport?.Invoke(value);
    }
}
