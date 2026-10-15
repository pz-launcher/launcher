using PzLauncher.Core.Common;

namespace PzLauncher.Core.Java;

internal static class JavaErrors
{
    public static Error InvalidPath(string path) =>
        new(ErrorCode.JavaInvalidPath, $"Not a valid absolute path: '{path}'", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["path"] = path },
        };

    public static Error PathNotFound(string path) =>
        new(ErrorCode.JavaPathNotFound, $"Directory does not exist: {path}", ErrorKind.NotFound)
        {
            Details = new Dictionary<string, string> { ["path"] = path },
        };

    public static Error NotAJavaHome(string path) =>
        new(ErrorCode.JavaNotAJavaHome, $"Directory is not a Java home or its bin folder: {path}", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["path"] = path },
        };

    public static Error Not64Bit(string path) =>
        new(ErrorCode.JavaNot64Bit, $"Java runtime is not 64-bit: {path}", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["path"] = path },
        };

    /// <param name="path">A folder, or a registry key such as <c>HKLM\SOFTWARE\JavaSoft\JDK</c>.</param>
    public static Error SourceUnreadable(string path, string reason) =>
        new(ErrorCode.JavaSourceUnreadable, $"Java search location could not be read: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error ReleaseUnreadable(string path, string reason) =>
        new(ErrorCode.JavaReleaseUnreadable, $"Java release file could not be read: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };
}
