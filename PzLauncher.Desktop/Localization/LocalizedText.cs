using System;
using PzLauncher.Core.Common;

namespace PzLauncher.Desktop.Localization;

/// <summary>A shown text whose source is kept, so a language switch can render it again.</summary>
public sealed class LocalizedText(Action<string?> set)
{
    private Func<string>? _source;

    public void Show(Func<string> source)
    {
        _source = source;
        set(source());
    }

    /// <summary>A null error clears the text.</summary>
    public void Show(Error? error)
    {
        if (error is null)
        {
            Clear();
            return;
        }

        Show(() => ErrorMessages.ToUserText(error));
    }

    public void Clear()
    {
        _source = null;
        set(null);
    }

    public void Render() => set(_source?.Invoke());
}
