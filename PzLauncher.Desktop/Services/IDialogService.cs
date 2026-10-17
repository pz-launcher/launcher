using System.Threading.Tasks;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;

namespace PzLauncher.Desktop.Services;

/// <summary>Every dialog is modal over the main window and completes when the user closes it.</summary>
public interface IDialogService
{
    Task ShowErrorAsync(string message);

    /// <summary><c>true</c> only for the confirm button; closing the window is cancel.</summary>
    Task<bool> ConfirmAsync(ConfirmPrompt prompt);

    Task<TextPromptResult> PromptTextAsync(TextPrompt prompt);

    /// <summary>Starts the operation when it opens. <c>true</c> when the user chose to launch the game.</summary>
    Task<bool> ShowZombieBuddySetupAsync(ZombieBuddySetupViewModel setup);
}
