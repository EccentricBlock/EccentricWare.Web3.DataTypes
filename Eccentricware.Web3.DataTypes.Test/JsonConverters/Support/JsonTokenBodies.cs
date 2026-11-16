using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>How a token's text is written inside a JSON string, for the shape tests.</summary>
internal enum TokenVariant
{
    /// <summary>The text as is: contiguous and unescaped, the converters' fast path.</summary>
    Plain,

    /// <summary>First character as a <c>\uXXXX</c> escape.</summary>
    EscapedFirst,

    /// <summary>Every character as a <c>\uXXXX</c> escape.</summary>
    EscapedAll,

    /// <summary>Literal spaces around the text (still unescaped).</summary>
    PaddedSpaces,

    /// <summary>Tab, CR, LF and space around the text, written as escapes.</summary>
    PaddedEscapedWhitespace,
}

/// <summary>Builds JSON string bodies and documents from token text.</summary>
internal static class JsonTokenBodies
{
    public static readonly TokenVariant[] AllVariants = Enum.GetValues<TokenVariant>();

    public static string Escape(char c) => $"\\u{(int)c:x4}";

    public static string EscapeFirst(string text) => text.Length == 0 ? text : Escape(text[0]) + text[1..];

    public static string EscapeAll(string text) => string.Concat(text.Select(Escape));

    public static string Apply(string text, TokenVariant variant) => variant switch
    {
        TokenVariant.Plain => text,
        TokenVariant.EscapedFirst => EscapeFirst(text),
        TokenVariant.EscapedAll => EscapeAll(text),
        TokenVariant.PaddedSpaces => "  " + text + "   ",
        TokenVariant.PaddedEscapedWhitespace => "\\t\\r\\n " + text + " \\n\\t",
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    /// <summary>Escaped (first character) text followed by spaces so the raw token is exactly <paramref name="rawLength"/> bytes.</summary>
    public static string EscapedPaddedToRawLength(string text, int rawLength)
    {
        string head = EscapeFirst(text);
        return head + new string(' ', rawLength - head.Length);
    }

    public static byte[] Value(string body) => Encoding.UTF8.GetBytes("\"" + body + "\"");

    /// <summary>An object with one property named by <paramref name="body"/>, value 7.</summary>
    public static byte[] Key(string body) => Encoding.UTF8.GetBytes("{\"" + body + "\":7}");

    public static string Quote(string text) => "\"" + text + "\"";
}
