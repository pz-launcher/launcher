using System.Threading.Tasks;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;

namespace PzLauncher.Desktop.Services;

public interface IDialogService
{
    /// <summary>Modal over the main window; completes when the user closes it.</summary>
    Task ShowErrorAsync(string message);

    /// <summary>Modal over the main window; starts the operation when it opens. <c>true</c> when the user chose to launch the game.</summary>
    Task<bool> ShowZombieBuddySetupAsync(ZombieBuddySetupViewModel setup);
}
