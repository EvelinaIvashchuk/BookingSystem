using System.Text.RegularExpressions;

namespace BookingSystem.Ai;

/// <summary>
/// The widget renders replies as plain text, but models occasionally emit Markdown despite the
/// instructions. Strips the common markers so the user never sees "**" or "###".
/// </summary>
public static partial class ReplyFormatter
{
    public static string ToPlainText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        text = BoldRegex().Replace(text, "$2");
        text = HeadingRegex().Replace(text, string.Empty);
        text = BulletRegex().Replace(text, "$1- ");
        return text.Trim();
    }

    [GeneratedRegex(@"(\*\*|__)(.+?)\1", RegexOptions.Singleline)]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"^[ \t]*#{1,6}[ \t]+", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^([ \t]*)[*•][ \t]+", RegexOptions.Multiline)]
    private static partial Regex BulletRegex();
}
