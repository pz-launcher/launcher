using System.Collections.Generic;

namespace PzLauncher.Desktop.Services.Shell.Dialogs;

/// <param name="Hint">Shown under the options.</param>
/// <param name="Preselected">The option checked when the dialog opens.</param>
public sealed record ChoicePrompt<T>(string Title, string Message, string Hint, IReadOnlyList<ChoiceOption<T>> Options, T Preselected)
    where T : struct;

public sealed record ChoiceOption<T>(T Value, string Text)
    where T : struct;
