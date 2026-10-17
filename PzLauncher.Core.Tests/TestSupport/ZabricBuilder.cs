using System.IO.Compression;

namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>Zabric copies as Workshop ships them: <c>&lt;mod folder&gt;/&lt;version&gt;/{mod.info, media/java/Zabric.jar}</c>.</summary>
public static class ZabricBuilder
{
    public const string PreloadManifest = "Manifest-Version: 1.0\r\nZB-Preload: true\r\n\r\n";

    public static string ModInfo(string id = "Zabric", string preload = "true") =>
        $"name=Zabric\nid={id}\njavaJarFile=media/java/Zabric.jar\njavaPkgName=com.pzlauncher.zabric\njavaPreload={preload}\n";

    /// <summary>Returns the version folder.</summary>
    public static string Create(string modFolder, string version = "42", string? modInfo = null, string? manifest = PreloadManifest, string classes = "v1")
    {
        var folder = Directory.CreateDirectory(Path.Combine(modFolder, version)).FullName;
        File.WriteAllText(InfPath(folder), modInfo ?? ModInfo());
        WriteJar(JarPath(folder), manifest, classes);
        return folder;
    }

    public static string InfPath(string versionFolder) => Path.Combine(versionFolder, "mod.info");

    public static string JarPath(string versionFolder) => Path.Combine(versionFolder, "media", "java", "Zabric.jar");

    /// <summary><paramref name="classes"/> stands in for the class files, so equal values give byte-identical jars.</summary>
    public static void WriteJar(string path, string? manifest, string classes = "v1")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Delete(path);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            if (manifest is not null)
            {
                Add(archive, "META-INF/MANIFEST.MF", manifest);
            }

            Add(archive, "com/pzlauncher/zabric/Zabric.class", classes);
        }
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);

        // A fixed timestamp keeps jars with the same content byte-identical.
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
