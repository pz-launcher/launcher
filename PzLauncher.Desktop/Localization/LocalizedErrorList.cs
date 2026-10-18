using System.Collections.Generic;
using System.Collections.ObjectModel;
using PzLauncher.Core.Common;

namespace PzLauncher.Desktop.Localization;

/// <summary>Shown error texts whose errors are kept, so a language switch can render them again.</summary>
public sealed class LocalizedErrorList
{
    private readonly List<Error> _sources = [];

    public ObservableCollection<string> Items { get; } = [];

    public void Add(Error error)
    {
        _sources.Add(error);
        Items.Add(ErrorMessages.ToUserText(error));
    }

    public void Clear()
    {
        _sources.Clear();
        Items.Clear();
    }

    public bool Contains(ErrorCode code) => _sources.Exists(error => error.Code == code);

    public void Render()
    {
        for (var i = 0; i < _sources.Count; i++)
        {
            Items[i] = ErrorMessages.ToUserText(_sources[i]);
        }
    }
}
