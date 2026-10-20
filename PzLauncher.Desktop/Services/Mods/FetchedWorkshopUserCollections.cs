using System;
using PzLauncher.Core.Steam.Workshop.Actions;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>The last answer Steam gave for the account's favorited and published collections, and when it gave it.</summary>
public sealed record FetchedWorkshopUserCollections(WorkshopUserCollections Collections, DateTimeOffset FetchedAt);
