using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>Runs an action while the current user can't list <c>dir</c>; skips the test when running as root.</summary>
public static class DeniedListing
{
    public static Task<T> RunAsync<T>(string dir, Func<Task<T>> action) =>
        OperatingSystem.IsWindows() ? WithDeniedListingAsync(dir, action) : WithNoUnixPermissionsAsync(dir, action);

    [SupportedOSPlatform("windows")]
    private static async Task<T> WithDeniedListingAsync<T>(string dir, Func<Task<T>> action)
    {
        var info = new DirectoryInfo(dir);
        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(rule);
        info.SetAccessControl(acl);
        try
        {
            return await action();
        }
        finally
        {
            acl = info.GetAccessControl();
            acl.RemoveAccessRule(rule);
            info.SetAccessControl(acl);
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static async Task<T> WithNoUnixPermissionsAsync<T>(string dir, Func<Task<T>> action)
    {
        File.SetUnixFileMode(dir, UnixFileMode.None);
        try
        {
            Assert.SkipWhen(CanList(dir), "Running as a user that ignores permissions (root).");
            return await action();
        }
        finally
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static bool CanList(string dir)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(dir).Any();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
