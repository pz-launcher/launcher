using System;
using System.Threading.Tasks;

namespace PzLauncher.Desktop.Services.Shell;

public interface IUrlLauncher
{
    /// <summary>Opens the URL with the OS handler of its scheme (browser, Steam client); <c>false</c> when the OS refused.</summary>
    Task<bool> OpenAsync(Uri url);
}
