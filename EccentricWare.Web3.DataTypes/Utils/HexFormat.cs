using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Parses the hex format specifiers shared by <see cref="Hash32"/> and <see cref="HexBytes"/> without allocating.
/// </summary>
internal static class HexFormat
{
    /// <summary>
    /// Maps a format specifier to its flags:
    /// "" (default) and "0x" = prefix + lowercase; "0X" = prefix + uppercase digits; "x" = lowercase; "X" = uppercase.
    /// Throws <see cref="FormatException"/> for any other specifier.
    /// Every type writes the prefix as lowercase "0x" (the form required by Ethereum JSON-RPC), even for "0X".
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Parse(ReadOnlySpan<char> format, out bool includePrefix, out bool uppercase)
    {
        switch (format.Length)
        {
            case 0:
                includePrefix = true;
                uppercase = false;
                return;

            case 1 when format[0] == 'x':
                includePrefix = false;
                uppercase = false;
                return;

            case 1 when format[0] == 'X':
                includePrefix = false;
                uppercase = true;
                return;

            case 2 when format[0] == '0' && format[1] == 'x':
                includePrefix = true;
                uppercase = false;
                return;

            case 2 when format[0] == '0' && format[1] == 'X':
                includePrefix = true;
                uppercase = true;
                return;
        }

        ThrowHelper.ThrowFormatExceptionUnknownFormat(format);
        includePrefix = false;
        uppercase = false;
    }
}
