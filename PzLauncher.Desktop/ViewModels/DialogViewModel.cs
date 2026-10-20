using System;

namespace PzLauncher.Desktop.ViewModels;

/// <summary>A dialog's ViewModel that closes its window with a result.</summary>
public abstract class DialogViewModel : ViewModelBase
{
    /// <summary>The argument is the dialog's result; closing the window any other way returns <c>false</c>.</summary>
    public event EventHandler<bool>? CloseRequested;

    protected void RequestClose(bool result) => CloseRequested?.Invoke(this, result);
}
