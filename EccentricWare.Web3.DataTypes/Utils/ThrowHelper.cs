using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Utils;


/// <summary>
/// Helper class to throw exceptions outside hot paths.
/// Keeps throwing code out of inlined methods for better JIT optimization.
/// Consolidated for all data types (Hash32, Address, etc.).
/// </summary>
internal static class ThrowHelper
{
    /// <summary>
    /// Throws an <see cref="ArgumentNullException"/>.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentNullException(string paramName)
        => throw new ArgumentNullException(paramName);

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> for an invalid function selector length.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentExceptionInvalidFunctionSelectorLength(string paramName)
        => throw new ArgumentException("Function selector must be at least 4 bytes.", paramName);

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> for an invalid discriminator length.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentExceptionInvalidDiscriminatorLength(string paramName)
        => throw new ArgumentException("Instruction discriminator must be at least 8 bytes.", paramName);

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> when a destination span is too small.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentExceptionDestinationTooSmall(string paramName)
        => throw new ArgumentException("Destination buffer is too small.", paramName);

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> when an argument is the wrong runtime type.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentExceptionWrongType(string paramName, string expectedTypeName)
        => throw new ArgumentException($"Object must be of type {expectedTypeName}.", paramName);

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> when a fixed-size span is the wrong length.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentExceptionFixedSize(string paramName, int requiredLength)
        => throw new ArgumentException($"Destination must be exactly {requiredLength} elements long.", paramName);

    /// <summary>
    /// Throws a <see cref="FormatException"/> for an invalid function selector.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowFormatExceptionInvalidFunctionSelector()
        => throw new FormatException("Invalid function selector hex value.");

    /// <summary>
    /// Throws a <see cref="FormatException"/> for an invalid instruction discriminator.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowFormatExceptionInvalidDiscriminator()
        => throw new FormatException("Invalid instruction discriminator hex value.");

    /// <summary>
    /// Throws a <see cref="FormatException"/> for an unsupported format string.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowFormatExceptionUnknownFormat(ReadOnlySpan<char> format)
        => throw new FormatException($"Unknown format: \"{format.ToString()}\"");

    /// <summary>
    /// Throws a <see cref="JsonException"/> because a string token exceeds the size the converters will buffer.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowJsonExceptionTokenTooLarge(string typeName, int maxLength)
        => throw new JsonException($"The JSON string token for {typeName} exceeds the {maxLength.ToString("N0", CultureInfo.InvariantCulture)}-byte limit.");

    /// <summary>
    /// Throws a <see cref="JsonException"/> because a dictionary key is larger than the type allows as a key.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowJsonExceptionKeyTooLarge(string typeName, int maxBytes)
        => throw new JsonException($"A {typeName} dictionary key exceeds the {maxBytes.ToString("N0", CultureInfo.InvariantCulture)}-byte limit.");

    /// <summary>
    /// Throws a <see cref="JsonException"/> for a token a converter cannot read: a JSON <c>null</c> (never silently mapped
    /// to a zero value; use a nullable type where absence matters) or any other non-string, non-number token.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowJsonExceptionUnexpectedToken(string typeName, JsonTokenType tokenType, bool numberAllowed)
    {
        if (tokenType == JsonTokenType.Null)
            throw new JsonException($"Cannot read null into {typeName}.");

        throw new JsonException(numberAllowed
            ? $"Expected JSON string or number for {typeName}."
            : $"Expected JSON string for {typeName}.");
    }

    /// <summary>
    /// Throws a <see cref="JsonException"/> indicating an invalid value for the target type.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowJsonExceptionInvalidValue(string typeName)
        => throw new JsonException($"Invalid value for {typeName}.");

    /// <summary>
    /// Throws a <see cref="JsonException"/> because a value could not be formatted into the converter's buffer.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowJsonExceptionFormatFailed(string typeName)
        => throw new JsonException($"Failed to format {typeName}.");

    [DoesNotReturn]
    public static void ThrowFormatExceptionInvalidBase64()
     => throw new FormatException("Invalid Base64 encoded value.");

    [DoesNotReturn]
    public static void ThrowFormatExceptionInvalidAddress()
        => throw new FormatException("Invalid blockchain address format");


    // Hash32 exceptions
    [DoesNotReturn]
    public static void ThrowArgumentExceptionInvalidLength(string paramName)
        => throw new ArgumentException($"Hash32 requires exactly {Hash32.ByteLength} bytes", paramName);


    [DoesNotReturn]
    public static void ThrowFormatExceptionInvalidHexLength()
        => throw new FormatException($"Hash32 requires exactly {Hash32.HexLength} hex characters (32 bytes)");

    [DoesNotReturn]
    public static void ThrowFormatExceptionInvalidHex()
        => throw new FormatException("Invalid hex character");

    // Address exceptions
    [DoesNotReturn]
    public static void ThrowArgumentExceptionInvalidEvmLength(string paramName)
        => throw new ArgumentException($"EVM address requires exactly {Address.EvmByteLength} bytes", paramName);

    [DoesNotReturn]
    public static void ThrowArgumentExceptionInvalidSolanaLength(string paramName)
        => throw new ArgumentException($"Solana address requires exactly {Address.SolanaByteLength} bytes", paramName);

    [DoesNotReturn]
    public static void ThrowFormatExceptionInvalidBase58()
        => throw new FormatException("Invalid Base58 string for Solana address");

    // Signature exceptions
    [DoesNotReturn]
    public static void ThrowArgumentExceptionInvalidSignatureRLength(string paramName)
        => throw new ArgumentException("Signature r component requires exactly 32 bytes", paramName);

    [DoesNotReturn]
    public static void ThrowArgumentExceptionInvalidSignatureSLength(string paramName)
        => throw new ArgumentException("Signature s component requires exactly 32 bytes", paramName);

    [DoesNotReturn]
    public static void ThrowArgumentExceptionInvalidEvmSignatureLength(string paramName)
        => throw new ArgumentException($"EVM signature requires exactly {Signature.EvmByteLength} bytes", paramName);

    [DoesNotReturn]
    public static void ThrowArgumentExceptionInvalidSolanaSignatureLength(string paramName)
        => throw new ArgumentException($"Solana signature requires exactly {Signature.SolanaByteLength} bytes", paramName);

    [DoesNotReturn]
    public static void ThrowFormatExceptionInvalidSignature()
        => throw new FormatException($"Invalid signature: expected hex with an optional 0x prefix ({Signature.EvmHexLength} digits for EVM, {Signature.SolanaHexLength} for Solana) or Solana Base58 without a prefix");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionBase58RequiresSolanaSignature()
        => throw new InvalidOperationException("Base58 is only defined for Solana signatures; this is an EVM signature.");

    // RLP exceptions (programming-contract violations only; malformed input is reported through RLPStatus)
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionRlpEnumeratorNoCurrent()
        => throw new InvalidOperationException("RLPChildEnumerator.Current is only valid after MoveNext() returned true and before the next MoveNext() returns false.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionRlpDocumentModified()
        => throw new InvalidOperationException("The RLP document no longer matches what was validated: its bytes were modified after TryDecodeComplete.");
}
