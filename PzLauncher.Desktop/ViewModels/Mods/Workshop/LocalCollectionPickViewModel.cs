using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Desktop.Services.Mods;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>One per dialog before Workshop items are added to a local collection: an existing one, or a new one by name.</summary>
public sealed partial class LocalCollectionPickViewModel : DialogViewModel
{
    private readonly LocalCollections _collections;

    public LocalCollectionPickViewModel(LocalCollections collections)
    {
        _collections = collections;
        Options = [.. collections.All.Select(collection => new LocalCollectionPickOption(collection.Id, collection.Name, collection.Items.Count, OnOptionChecked))];
        if (Options.Count > 0)
        {
            Options[0].IsChecked = true;
        }
        else
        {
            IsNewChecked = true;
        }
    }

    public IReadOnlyList<LocalCollectionPickOption> Options { get; }

    public bool HasOptions => Options.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewNameError))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial bool IsNewChecked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewNameError))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial string NewName { get; set; } = "";

    /// <summary>Shown once a name is typed for a new collection; <c>null</c> while the box is empty or the name is free.</summary>
    public string? NewNameError => IsNewChecked && NewName.Length > 0 ? LocalCollectionPrompts.ValidateName(_collections, NewName) : null;

    /// <summary>Set by accept; <c>null</c> until then.</summary>
    public LocalCollectionTarget? Target { get; private set; }

    private bool CanAccept() =>
        IsNewChecked ? LocalCollectionPrompts.ValidateName(_collections, NewName) is null : Options.Any(option => option.IsChecked);

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept()
    {
        Target = IsNewChecked
            ? new LocalCollectionTarget.New(NewName.Trim())
            : Options.Where(option => option.IsChecked).Select(option => new LocalCollectionTarget.Existing(option.Id)).FirstOrDefault();
        if (Target is not null)
        {
            RequestClose(true);
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose(false);

    // Typing a name picks the new collection.
    partial void OnNewNameChanged(string value) => IsNewChecked = true;

    partial void OnIsNewCheckedChanged(bool value)
    {
        if (value)
        {
            foreach (var option in Options)
            {
                option.IsChecked = false;
            }
        }
    }

    private void OnOptionChecked(LocalCollectionPickOption checkedOption)
    {
        if (!checkedOption.IsChecked)
        {
            AcceptCommand.NotifyCanExecuteChanged();
            return;
        }

        IsNewChecked = false;
        foreach (var option in Options.Where(option => option != checkedOption))
        {
            option.IsChecked = false;
        }

        AcceptCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class LocalCollectionPickOption(Guid id, string name, int itemCount, Action<LocalCollectionPickOption> checkedChanged) : ObservableObject
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    public int ItemCount { get; } = itemCount;

    [ObservableProperty] public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value) => checkedChanged(this);
}

/// <summary>Where picked Workshop items go.</summary>
public abstract record LocalCollectionTarget
{
    private LocalCollectionTarget()
    {
    }

    public sealed record Existing(Guid Id) : LocalCollectionTarget;

    /// <param name="Name">Trimmed, valid and free.</param>
    public sealed record New(string Name) : LocalCollectionTarget;
}
