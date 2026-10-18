using System.Text;
using PzLauncher.Core.Common;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Common;

public sealed class AtomicFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReplaceWithBackupAsync_ExistingFile_KeepsTheOriginalBytesAsTheBackup()
    {
        var path = Path.Combine(_temp.Path, "file.txt");
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a:\r\nB42·通用合集\n")];
        await File.WriteAllBytesAsync(path, original, Ct);
        var content = Encoding.UTF8.GetBytes("new\n");

        await AtomicFile.ReplaceWithBackupAsync(path, original, content, Ct);

        Assert.Equal(original, await File.ReadAllBytesAsync(path + AtomicFile.BackupSuffix, Ct));
        Assert.Equal(content, await File.ReadAllBytesAsync(path, Ct));
    }
}
