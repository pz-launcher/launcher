using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Core.Steam.Workshop.Ids;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Mods.Modpacks;

namespace PzLauncher.Desktop.ViewModels.Mods.Modpacks;

/// <summary>A Steam Workshop collection the modpack dialog offers, from a list that keeps collections.</summary>
/// <param name="Title"><c>null</c> when unknown.</param>
/// <param name="Source">The list it was found in first.</param>
public sealed record ModpackSteamOption(ulong Id, string? Title, string Source);

/// <summary>What the modpack dialog accepted.</summary>
/// <param name="Name">Trimmed, valid and free.</param>
public sealed record ModpackEditResult(string Name, ModpackPreset Preset, ModpackCollection Collection);

/// <summary>
/// One per dialog for a new modpack or an edit of one: its name, one of the install's mod presets and a collection, Steam (from a list or a
/// pasted link) or local. An independent modpack also offers its own copies, so an edit can keep them or link it again.
/// </summary>
public sealed partial class ModpackEditViewModel : DialogViewModel
{
    private readonly Func<string, string?> _validateName;

    /// <param name="original"><c>null</c> for a new modpack.</param>
    /// <param name="validateName">The problem with a name; <c>null</c> when it is free.</param>
    public ModpackEditViewModel(
        string title,
        Modpack? original,
        Func<string, string?> validateName,
        IReadOnlyList<ModCollection> presets,
        IReadOnlyList<ModpackSteamOption> steamCollections,
        IReadOnlyList<LocalCollection> localCollections)
    {
        Title = title;
        _validateName = validateName;

        List<ModpackEditOption<ModpackPreset>> presetOptions =
        [
            .. presets.Select(preset => new ModpackEditOption<ModpackPreset>(
                preset.Name,
                string.Format(CultureInfo.CurrentCulture, UiText.ModCollections_CountFormat, preset.ModIds.Count),
                new ModpackPreset(preset.Name, preset.ModIds, IsLinked: true))),
        ];
        if (original?.Preset is { IsLinked: false } presetCopy)
        {
            presetOptions.Insert(0, new ModpackEditOption<ModpackPreset>(presetCopy.Name, UiText.Modpacks_OwnCopy, presetCopy));
        }

        List<ModpackEditOption<ModpackCollection.Local>> localOptions =
        [
            .. localCollections.Select(collection => new ModpackEditOption<ModpackCollection.Local>(
                collection.Name,
                string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_ItemCountFormat, collection.Items.Count),
                new ModpackCollection.Local(collection.Id, collection.Name, collection.Items))),
        ];
        if (original?.Collection is ModpackCollection.Local { Linked: null } localCopy)
        {
            localOptions.Insert(0, new ModpackEditOption<ModpackCollection.Local>(localCopy.Name, UiText.Modpacks_OwnCopy, localCopy));
        }

        Presets = presetOptions;
        SteamCollections =
        [
            .. steamCollections.Select(option => new ModpackEditOption<ulong>(
                option.Title ?? option.Id.ToString(CultureInfo.InvariantCulture), option.Source, option.Id)),
        ];
        LocalCollections = localOptions;

        Name = original?.Name ?? "";
        Check(original is null
            ? Presets.FirstOrDefault()
            : Presets.FirstOrDefault(option => option.Value.IsLinked == original.Preset.IsLinked && option.Value.Name == original.Preset.Name));
        switch (original?.Collection)
        {
            case ModpackCollection.Local local:
                IsLocalChecked = true;
                Check(LocalCollections.FirstOrDefault(option => local.Linked is { } id ? option.Value.Linked == id : option.Value == local));
                break;
            case ModpackCollection.Steam steam when SteamCollections.FirstOrDefault(option => option.Value == steam.Id) is { } listed:
                IsSteamChecked = true;
                Check(listed);
                break;
            case ModpackCollection.Steam steam:
                SteamLinkText = steam.Id.ToString(CultureInfo.InvariantCulture);
                break;
            default:
                IsSteamChecked = true;
                break;
        }

        // Wired last, so preselecting asks nothing.
        Group(Presets, onChecked: null);
        Group(SteamCollections, () =>
        {
            IsSteamChecked = true;
            SteamLinkText = "";
        });
        Group(LocalCollections, () => IsLocalChecked = true);
    }

    public string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial string Name { get; set; } = "";

    /// <summary>Shown once a name is typed; <c>null</c> while the box is empty or the name is free.</summary>
    public string? NameError => Name.Length > 0 ? _validateName(Name) : null;

    public IReadOnlyList<ModpackEditOption<ModpackPreset>> Presets { get; }

    public bool HasPresets => Presets.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial bool IsSteamChecked { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial bool IsLocalChecked { get; set; }

    public IReadOnlyList<ModpackEditOption<ulong>> SteamCollections { get; }

    public bool HasSteamCollections => SteamCollections.Count > 0;

    /// <summary>A collection link or id, used instead of a listed collection.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamLinkError))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial string SteamLinkText { get; set; } = "";

    /// <summary>Text that is neither a link nor an id; <c>null</c> when empty or valid.</summary>
    public string? SteamLinkError => SteamLinkText.Trim().Length > 0 && !WorkshopLinks.TryParseItemId(SteamLinkText, out _) ? UiText.Workshop_LinkInvalid : null;

    public IReadOnlyList<ModpackEditOption<ModpackCollection.Local>> LocalCollections { get; }

    public bool HasLocalCollections => LocalCollections.Count > 0;

    /// <summary>Set by accept; <c>null</c> until then.</summary>
    public ModpackEditResult? Result { get; private set; }

    private bool CanAccept() => _validateName(Name) is null && CheckedPreset() is not null && CheckedCollection() is not null;

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept()
    {
        if (CheckedPreset() is { } preset && CheckedCollection() is { } collection && _validateName(Name) is null)
        {
            Result = new ModpackEditResult(Name.Trim(), preset, collection);
            RequestClose(true);
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose(false);

    partial void OnIsSteamCheckedChanged(bool value)
    {
        if (value)
        {
            IsLocalChecked = false;
        }
    }

    partial void OnIsLocalCheckedChanged(bool value)
    {
        if (value)
        {
            IsSteamChecked = false;
        }
    }

    // Typing a link picks it over a listed collection.
    partial void OnSteamLinkTextChanged(string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        IsSteamChecked = true;
        foreach (var option in SteamCollections)
        {
            option.IsChecked = false;
        }
    }

    private static void Check(ModpackEditOption? option)
    {
        if (option is not null)
        {
            option.IsChecked = true;
        }
    }

    private ModpackPreset? CheckedPreset() => Presets.FirstOrDefault(option => option.IsChecked)?.Value;

    private ModpackCollection? CheckedCollection()
    {
        if (IsLocalChecked)
        {
            return LocalCollections.FirstOrDefault(option => option.IsChecked)?.Value;
        }

        if (!IsSteamChecked)
        {
            return null;
        }

        if (SteamCollections.FirstOrDefault(option => option.IsChecked) is { } listed)
        {
            return new ModpackCollection.Steam(listed.Value);
        }

        return SteamLinkText.Trim().Length > 0 && WorkshopLinks.TryParseItemId(SteamLinkText, out var id) ? new ModpackCollection.Steam(id) : null;
    }

    // One checked option per group.
    private void Group<T>(IReadOnlyList<ModpackEditOption<T>> options, Action? onChecked)
    {
        foreach (var option in options)
        {
            option.PropertyChanged += (_, e) => OnOptionChanged(options, option, e, onChecked);
        }
    }

    private void OnOptionChanged<T>(IReadOnlyList<ModpackEditOption<T>> options, ModpackEditOption<T> option, PropertyChangedEventArgs e, Action? onChecked)
    {
        if (e.PropertyName != nameof(ModpackEditOption.IsChecked))
        {
            return;
        }

        if (option.IsChecked)
        {
            foreach (var other in options.Where(other => other != option))
            {
                other.IsChecked = false;
            }

            onChecked?.Invoke();
        }

        AcceptCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>A radio choice of the modpack dialog.</summary>
/// <param name="detail">Shown muted after the text: a count, the list it came from, or that it is the modpack's own copy.</param>
public abstract partial class ModpackEditOption(string text, string detail) : ObservableObject
{
    public string Text { get; } = text;

    public string Detail { get; } = detail;

    [ObservableProperty] public partial bool IsChecked { get; set; }
}

public sealed class ModpackEditOption<T>(string text, string detail, T value) : ModpackEditOption(text, detail)
{
    public T Value { get; } = value;
}
