namespace PzLauncher.Desktop.Services.Shell;

public interface IAppLifetime
{
    /// <summary>Closes the main window the way the user would, so pending saves are flushed before the app exits.</summary>
    void Close();
}
