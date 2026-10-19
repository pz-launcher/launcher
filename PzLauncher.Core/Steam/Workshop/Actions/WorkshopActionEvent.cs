using System.Text.Json.Serialization;

namespace PzLauncher.Core.Steam.Workshop.Actions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(WorkshopActionStarted), "started")]
[JsonDerivedType(typeof(WorkshopItemProgress), "progress")]
[JsonDerivedType(typeof(WorkshopItemFinished), "itemFinished")]
public abstract record WorkshopActionEvent;

/// <summary>Steam accepted the helper; items follow.</summary>
public sealed record WorkshopActionStarted : WorkshopActionEvent;

/// <param name="Total">0 until Steam knows the download size.</param>
public sealed record WorkshopItemProgress(ulong Id, ulong Bytes, ulong Total) : WorkshopActionEvent;

public sealed record WorkshopItemFinished(WorkshopItemOutcome Outcome) : WorkshopActionEvent;
