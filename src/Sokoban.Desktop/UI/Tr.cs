using System.Globalization;
using Jentl.Core.Content;
using Jentl.Core.Localization;
using Jentl.Core.UI;

namespace Sokoban.Desktop.UI;

/// <summary>
/// The game's text in the player's language. Write <c>T("Select level")</c>, <c>T("{0} wins", name)</c> or
/// <c>N(count, "{0} move", "{0} moves")</c> with the English text; translations live in
/// <c>Assets/lang/&lt;language&gt;.json</c>, and anything untranslated shows in English.
/// </summary>
internal static class Tr
{
    /// <summary>The languages there are files for (English needs none), with their names in their own language.</summary>
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } = [("en", "English"), ("nl", "Nederlands")];

    public static Localizer Current { get; private set; } = Localizer.English;

    public static CultureInfo Culture => Current.Culture;

    public static string T(string text) => Current[text];

    public static string T(string text, params object?[] args) => Current.Format(text, args);

    /// <summary>Singular or plural by <paramref name="count"/>, which fills <c>{0}</c>.</summary>
    public static string N(int count, string one, string other, params object?[] args) => Current.Plural(count, one, other, args);

    /// <summary>
    /// Switches to <paramref name="language"/>: a code, or "auto" for the system's (the browser's) language. A
    /// language whose file is missing or broken falls back to English. <paramref name="read"/> reads the file
    /// (the asset store, to see edits while the game runs).
    /// </summary>
    public static void Use(string language, Func<string, string>? read = null)
    {
        var code = language == "auto"
            ? Localizer.Pick(CultureInfo.CurrentUICulture, Languages.Select(l => l.Code))
            : language;
        try
        {
            Current = code == "en" ? Localizer.English : Localizer.Load(code, (read ?? GameFiles.ReadAllText)($"Assets/lang/{code}.json"));
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            Current = Localizer.English;
        }

        UiText.On = T("On");
        UiText.Off = T("Off");
    }
}
