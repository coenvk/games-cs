namespace Sokoban.Desktop;

/// <param name="StartLevel">0-based level to open directly, above the main menu.</param>
/// <param name="ScreenshotPath">Save a screenshot here once the script has played out, then exit.</param>
/// <param name="Script">Key names to press one by one instead of reading the keyboard; <c>Wait</c> idles half a second.</param>
internal sealed record GameOptions(int? StartLevel = null, string? ScreenshotPath = null, IReadOnlyList<string>? Script = null);
