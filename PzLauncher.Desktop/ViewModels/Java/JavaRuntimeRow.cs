using System;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Java;

namespace PzLauncher.Desktop.ViewModels.Java;

public sealed partial class JavaRuntimeRow
{
    private readonly Action<JavaRuntimeRow> _remove;

    internal JavaRuntimeRow(string homePath, JavaRuntime? runtime, bool isManual, Action<JavaRuntimeRow> remove)
    {
        HomePath = homePath;
        Runtime = runtime;
        IsManual = isManual;
        _remove = remove;
    }

    public string HomePath { get; }

    /// <summary><c>null</c>: a manually added folder that is no longer a usable Java (shown as missing, not selectable).</summary>
    public JavaRuntime? Runtime { get; }

    public bool IsManual { get; }

    [MemberNotNullWhen(true, nameof(Runtime))]
    public bool IsAvailable => Runtime is not null;

    public JavaRuntimeSource Source => Runtime?.Source ?? JavaRuntimeSource.Manual;

    public string? Version => Runtime?.Version?.Full;

    public string? Vendor => Runtime?.Vendor;

    public bool IsVersionUnknown => IsAvailable && Runtime.Version is null;

    public bool IsVendorUnknown => IsAvailable && Runtime.Vendor is null;

    [RelayCommand]
    private void Remove() => _remove(this);
}
