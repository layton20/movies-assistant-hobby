using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieAssistant.Api.Chat;

public static partial class UserInputSanitiser
{
    [GeneratedRegex(@"<(\s*/?\s*user_message[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex DelimiterTag();

    /// <summary>
    /// Removes invisible control/format characters (zero-width, Unicode tag characters, etc.)
    /// that can hide instructions, and defuses any attempt to open or close the
    /// &lt;user_message&gt; delimiter so the input can't break out of its wrapper.
    /// </summary>
    public static string Clean(string content)
    {
        var visible = new StringBuilder(content.Length);
        foreach (var rune in content.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var invisible = category is UnicodeCategory.Control or UnicodeCategory.Format
                && rune.Value is not ('\n' or '\r' or '\t' or 0x200D); // keep newlines and emoji joiner

            if (!invisible)
                visible.Append(rune.ToString());
        }

        return DelimiterTag().Replace(visible.ToString(), "&lt;$1&gt;");
    }
}
