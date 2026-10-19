using System;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

public sealed partial class WorkshopItemRow(WorkshopItem item, bool isSelected, Action<WorkshopItemRow> selectionChanged) : ObservableObject
{
    private const double BytesPerMegabyte = 1024 * 1024;

    public WorkshopItem Item { get; } = item;

    public ulong Id => Item.Id;

    public string IdText { get; } = item.Id.ToString(CultureInfo.InvariantCulture);

    /// <summary>The Web API title, else the item id.</summary>
    public string Title { get; } = TitleOf(item);

    public bool HasTitle => WebTitle(Item) is not null;

    public string ModIds { get; } = string.Join(", ", item.Mods.Select(mod => mod.Id));

    /// <summary><c>null</c> when not installed.</summary>
    public double? SizeMb { get; } = item.Installed is { } installed ? installed.Size / BytesPerMegabyte : null;

    /// <summary>The installed copy's version, else the latest one known; local time.</summary>
    public DateTime? Updated { get; } = (item.Installed?.Updated ?? item.Latest?.Updated)?.LocalDateTime;

    public string CollectionNames { get; } = string.Join(", ", item.Collections);

    public bool NeedsUpdate => Item.State == WorkshopItemState.NeedsUpdate;

    [ObservableProperty] public partial bool IsSelected { get; set; } = isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadPercent), nameof(IsDownloadingWithoutPercent))]
    public partial bool IsDownloading { get; private set; }

    /// <summary>0 to 100; <c>null</c> until Steam knows the download size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadPercent), nameof(IsDownloadingWithoutPercent))]
    public partial int? DownloadPercent { get; private set; }

    public bool HasDownloadPercent => IsDownloading && DownloadPercent is not null;

    public bool IsDownloadingWithoutPercent => IsDownloading && DownloadPercent is null;

    internal void ShowDownload(ulong bytes, ulong total)
    {
        DownloadPercent = ProgressPercent.Of(bytes, total);
        IsDownloading = true;
    }

    internal void ClearDownload()
    {
        IsDownloading = false;
        DownloadPercent = null;
    }

    internal static string TitleOf(WorkshopItem item) => WebTitle(item) ?? item.Id.ToString(CultureInfo.InvariantCulture);

    internal static string? WebTitle(WorkshopItem item) => item.Latest is { Title.Length: > 0 } latest ? latest.Title : null;

    partial void OnIsSelectedChanged(bool value) => selectionChanged(this);
}
