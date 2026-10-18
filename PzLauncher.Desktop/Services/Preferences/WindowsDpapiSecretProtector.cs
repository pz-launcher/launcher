using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PzLauncher.Desktop.Services.Preferences;

/// <summary>Windows DPAPI, current-user scope: only this Windows account on this machine can decrypt it.</summary>
/// <remarks>Hand-written P/Invoke instead of the ProtectedData package: no dependency, and certain to work under Native AOT.</remarks>
public sealed partial class WindowsDpapiSecretProtector : ISecretProtector
{
    /// <summary>Persisted: never change it.</summary>
    public const string Prefix = "dpapi:";

    // Fail instead of showing a prompt; there is never a user to answer it.
    private const int CryptProtectUiForbidden = 0x1;

    public string? Protect(string secret)
    {
        var protectedBytes = Transform(Encoding.UTF8.GetBytes(secret), protect: true);
        return protectedBytes is null ? null : Prefix + Convert.ToBase64String(protectedBytes);
    }

    public string? Unprotect(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var base64 = stored.AsSpan(Prefix.Length);
        var buffer = new byte[base64.Length * 3 / 4];
        if (!Convert.TryFromBase64Chars(base64, buffer, out var length))
        {
            return null;
        }

        var plain = Transform(buffer.AsSpan(0, length).ToArray(), protect: false);
        return plain is null ? null : Encoding.UTF8.GetString(plain);
    }

    private static byte[]? Transform(byte[] input, bool protect)
    {
        var inputMemory = Marshal.AllocHGlobal(Math.Max(input.Length, 1));
        try
        {
            Marshal.Copy(input, 0, inputMemory, input.Length);
            var inputBlob = new DataBlob(input.Length, inputMemory);
            var ok = protect
                ? CryptProtectData(inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output)
                : CryptUnprotectData(inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!ok)
            {
                return null;
            }

            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputMemory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DataBlob(int size, IntPtr data)
    {
        public readonly int Size = size;
        public readonly IntPtr Data = data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(
        in DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        in DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
