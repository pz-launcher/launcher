namespace PzLauncher.Core.Zabric;

/// <summary>A <c>preload_mods</c> entry: absolute and normalized paths.</summary>
internal sealed record ZabricPreloadEntry(string InfPath, string JarPath);
