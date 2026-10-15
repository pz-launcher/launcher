namespace PzLauncher.Core.Java;

/// <param name="Full">As written in the <c>release</c> file, e.g. <c>21.0.9</c> or <c>1.8.0_392</c>.</param>
public sealed record JavaVersion(int Major, string Full);
