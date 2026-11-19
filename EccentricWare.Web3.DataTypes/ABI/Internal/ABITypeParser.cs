using System.Diagnostics.CodeAnalysis;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Bootstrap-time parser for canonical ABI type text and function signatures. Allocates freely (definitions, strings): never
/// call it on a request path. It is purely syntactic: it builds definitions and leaves every width, scale, length and limit
/// rule to <see cref="ABISchemaCompiler"/>, so each rule exists once. Whitespace and the word <c>tuple</c> are rejected, since
/// only canonical text is hashed into selectors. Tuple nesting is capped at <see cref="ABICompileLimits.HardMaxSchemaDepth"/>
/// so hostile text cannot exhaust the stack; array suffixes are parsed iteratively and capped at the same number (more can never compile), so text
/// length cannot drive allocation beyond that cap.
/// </summary>
internal static class ABITypeParser
{
    internal static bool TryParseType(string? text, [NotNullWhen(true)] out ABITypeDefinition? definition, out ABISchemaError error)
    {
        definition = null;
        if (text is null)
        {
            error = ABISchemaError.Argument(ABISchemaErrorReason.NullArgument);
            return false;
        }

        var parser = new Parser(text, 0);
        if (parser.ParseType(1, out definition) && parser.ExpectEnd())
        {
            error = ABISchemaError.None;
            return true;
        }

        definition = null;
        error = parser.Error;
        return false;
    }

    internal static bool TryParseTuple(string? text, [NotNullWhen(true)] out ABITupleDefinition? definition, out ABISchemaError error)
    {
        definition = null;
        if (text is null)
        {
            error = ABISchemaError.Argument(ABISchemaErrorReason.NullArgument);
            return false;
        }

        var parser = new Parser(text, 0);
        if (parser.ParseTuple(1, out definition) && parser.ExpectEnd())
        {
            error = ABISchemaError.None;
            return true;
        }

        definition = null;
        error = parser.Error;
        return false;
    }

    /// <summary>Parses <c>name(types)</c> into the name and the argument tuple.</summary>
    internal static bool TryParseFunctionSignature(
        string? signature,
        [NotNullWhen(true)] out string? name,
        [NotNullWhen(true)] out ABITupleDefinition? arguments,
        out ABISchemaError error)
    {
        name = null;
        arguments = null;
        if (signature is null)
        {
            error = ABISchemaError.Argument(ABISchemaErrorReason.NullArgument);
            return false;
        }

        int open = signature.IndexOf('(');
        if (open < 0)
        {
            error = ABISchemaError.Syntax(ABISchemaErrorReason.MalformedSyntax, signature.Length);
            return false;
        }

        string candidate = signature.Substring(0, open);
        if (!ABISelector.IsValidFunctionName(candidate))
        {
            error = ABISchemaError.Syntax(ABISchemaErrorReason.InvalidFunctionName, 0);
            return false;
        }

        var parser = new Parser(signature, open);
        if (parser.ParseTuple(1, out arguments) && parser.ExpectEnd())
        {
            name = candidate;
            error = ABISchemaError.None;
            return true;
        }

        arguments = null;
        error = parser.Error;
        return false;
    }

    private enum NumberResult { Ok, Malformed, Overflow }

    private sealed class Parser(string text, int position)
    {
        private int _pos = position;

        internal ABISchemaError Error { get; private set; }

        private char Peek() => _pos < text.Length ? text[_pos] : '\0';

        private bool Fail(ABIStatus status, ABISchemaErrorReason reason, int at)
        {
            Error = new ABISchemaError(status, reason, string.Empty, at);
            return false;
        }

        private bool Malformed(int at) => Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.MalformedSyntax, at);

        internal bool ExpectEnd() => _pos == text.Length || Malformed(_pos);

        internal bool ParseTuple(int depth, [NotNullWhen(true)] out ABITupleDefinition? tuple)
        {
            tuple = null;
            if (Peek() != '(')
            {
                return Malformed(_pos);
            }

            if (depth > ABICompileLimits.HardMaxSchemaDepth)
            {
                return Fail(ABIStatus.DepthLimitExceeded, ABISchemaErrorReason.SchemaTooDeep, _pos);
            }

            _pos++;
            var result = new ABITupleDefinition();
            if (Peek() == ')')
            {
                _pos++;
                tuple = result;
                return true;
            }

            while (true)
            {
                if (!ParseType(depth + 1, out var member))
                {
                    return false;
                }

                result.Add(member);
                char next = Peek();
                if (next == ',')
                {
                    _pos++;
                }
                else if (next == ')')
                {
                    _pos++;
                    tuple = result;
                    return true;
                }
                else
                {
                    return Malformed(_pos);
                }
            }
        }

        internal bool ParseType(int depth, [NotNullWhen(true)] out ABITypeDefinition? type)
        {
            type = null;
            ABITypeDefinition? current;
            if (Peek() == '(')
            {
                if (!ParseTuple(depth, out var tuple))
                {
                    return false;
                }

                current = ABITypeDefinition.Tuple(tuple);
            }
            else if (!ParseElementary(out current))
            {
                return false;
            }

            int suffixes = 0;
            while (Peek() == '[')
            {
                // More array suffixes than the hard schema depth can never compile (the compiler would reject the depth anyway), so stop
                // here instead of allocating one definition per suffix of hostile text (4M "[]" would otherwise cost about 192 MB).
                if (++suffixes > ABICompileLimits.HardMaxSchemaDepth)
                {
                    return Fail(ABIStatus.DepthLimitExceeded, ABISchemaErrorReason.SchemaTooDeep, _pos);
                }

                _pos++;
                int digitsStart = _pos;
                while (char.IsAsciiDigit(Peek()))
                {
                    _pos++;
                }

                int digitCount = _pos - digitsStart;
                if (Peek() != ']')
                {
                    return Malformed(_pos);
                }

                if (digitCount == 0)
                {
                    current = ABITypeDefinition.DynamicArray(current);
                }
                else
                {
                    switch (ParseNumber(text.AsSpan(digitsStart, digitCount), out int length))
                    {
                        case NumberResult.Malformed:
                            return Malformed(digitsStart);
                        case NumberResult.Overflow:
                            return Fail(ABIStatus.ArithmeticOverflow, ABISchemaErrorReason.SizeOverflow, digitsStart);
                    }

                    current = ABITypeDefinition.FixedArray(current, length);
                }

                _pos++; // ']'
            }

            type = current;
            return true;
        }

        private bool ParseElementary([NotNullWhen(true)] out ABITypeDefinition? type)
        {
            type = null;
            int start = _pos;
            while (_pos < text.Length && char.IsAsciiLetterOrDigit(text[_pos]))
            {
                _pos++;
            }

            ReadOnlySpan<char> token = text.AsSpan(start, _pos - start);
            if (token.IsEmpty)
            {
                return Malformed(start);
            }

            switch (token)
            {
                case "address": type = ABITypeDefinition.Address; return true;
                case "bool": type = ABITypeDefinition.Bool; return true;
                case "string": type = ABITypeDefinition.String; return true;
                case "function": type = ABITypeDefinition.Function; return true;
                case "bytes": type = ABITypeDefinition.Bytes; return true;
                case "uint": type = ABITypeDefinition.UInt(256); return true;
                case "int": type = ABITypeDefinition.Int(256); return true;
                case "fixed": type = ABITypeDefinition.Fixed(128, 18); return true;
                case "ufixed": type = ABITypeDefinition.UFixed(128, 18); return true;
            }

            if (token.StartsWith("uint", StringComparison.Ordinal))
            {
                return ParseWidth(token[4..], start, ABITypeKind.UInt, out type);
            }

            if (token.StartsWith("int", StringComparison.Ordinal))
            {
                return ParseWidth(token[3..], start, ABITypeKind.Int, out type);
            }

            if (token.StartsWith("bytes", StringComparison.Ordinal))
            {
                return ParseWidth(token[5..], start, ABITypeKind.FixedBytes, out type);
            }

            if (token.StartsWith("ufixed", StringComparison.Ordinal))
            {
                return ParseFixed(token[6..], start + 6, signed: false, out type);
            }

            if (token.StartsWith("fixed", StringComparison.Ordinal))
            {
                return ParseFixed(token[5..], start + 5, signed: true, out type);
            }

            return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.UnknownType, start);
        }

        private bool ParseWidth(ReadOnlySpan<char> digits, int tokenStart, ABITypeKind kind, [NotNullWhen(true)] out ABITypeDefinition? type)
        {
            type = null;
            switch (ParseNumber(digits, out int value))
            {
                case NumberResult.Malformed:
                    // "uintx", "int08", "bytesfoo": not a type name we know.
                    return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.UnknownType, tokenStart);
                case NumberResult.Overflow:
                    return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, tokenStart);
            }

            type = kind switch
            {
                ABITypeKind.UInt => ABITypeDefinition.UInt(value),
                ABITypeKind.Int => ABITypeDefinition.Int(value),
                _ => ABITypeDefinition.FixedBytes(value),
            };
            return true;
        }

        private bool ParseFixed(ReadOnlySpan<char> rest, int at, bool signed, [NotNullWhen(true)] out ABITypeDefinition? type)
        {
            type = null;
            int x = rest.IndexOf('x');
            if (x <= 0 || x == rest.Length - 1)
            {
                return Malformed(at);
            }

            var widthResult = ParseNumber(rest[..x], out int width);
            var scaleResult = ParseNumber(rest[(x + 1)..], out int scale);
            if (widthResult == NumberResult.Malformed || scaleResult == NumberResult.Malformed)
            {
                return Malformed(at);
            }

            if (widthResult == NumberResult.Overflow)
            {
                return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, at);
            }

            if (scaleResult == NumberResult.Overflow)
            {
                return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedPrecision, at + x + 1);
            }

            type = signed ? ABITypeDefinition.Fixed(width, scale) : ABITypeDefinition.UFixed(width, scale);
            return true;
        }

        /// <summary>Canonical decimal: ASCII digits only, no leading zero (except "0" itself), at most <see cref="int.MaxValue"/>.</summary>
        private static NumberResult ParseNumber(ReadOnlySpan<char> digits, out int value)
        {
            value = 0;
            if (digits.IsEmpty || (digits.Length > 1 && digits[0] == '0'))
            {
                return NumberResult.Malformed;
            }

            foreach (char c in digits)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return NumberResult.Malformed;
                }
            }

            long accumulated = 0;
            foreach (char c in digits)
            {
                accumulated = (accumulated * 10) + (c - '0');
                if (accumulated > int.MaxValue)
                {
                    return NumberResult.Overflow;
                }
            }

            value = (int)accumulated;
            return NumberResult.Ok;
        }
    }
}
