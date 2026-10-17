using System;
using System.Threading.Tasks;

namespace PzLauncher.Desktop.Services;

public interface IUrlLauncher
{
    /// <summary>Opens the URL in the default browser; <c>false</c> when the OS refused.</summary>
    Task<bool> OpenAsync(Uri url);
}
