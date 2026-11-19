using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Unicode;
using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Fuzz entry points for the ABI codec. Each is one deterministic function over arbitrary bytes that never throws for a correct
/// implementation and throws <see cref="InvalidOperationException"/> carrying the input in hex when an invariant breaks (a fuzzer
/// reports that exception as the crash). The raw-bytes formats and the seed naming are documented in <c>Fuzz/Corpus/README.md</c>.
/// <list type="bullet">
/// <item><see cref="Run"/>: <c>[schema index][document]</c>. Decodes the document under six limit sets and checks, for each: status and
/// diagnostic overloads agree, a failure resets every output, the status obeys the spec 5.2 precedence (invalid limits, size, structure),
/// <see cref="ABICallData.TryDecode"/> of the same bytes with a selector agrees with the document decode (and with a wrong or missing
/// selector), accepted under a tight set implies accepted under the generous one, and the decode calls allocate nothing. On the generous
/// set the verdict must equal the independent reference model's (permissive decode plus canonical re-encode). An accepted document is
/// then read through every locator path and every typed accessor (contract: false implies defaulted outputs and a legal status; invalid
/// paths give <see cref="ABIStatus.InvalidPath"/>), compared with the reference values, and re-encoded from the accessor values through
/// <see cref="ABIWriter"/>, <see cref="ABIMeasurer"/> and <see cref="ABICallEncoder"/>: measured length equals written length equals the
/// document length, the bytes are identical, bytes past the document are untouched, and every limit set that accepted the document
/// also lets the equal encode limits write it.</item>
/// <item><see cref="RunWriterScript"/>: <c>[schema index][events]</c> (see <see cref="ABIFuzzScript"/>). Replays the events through writer,
/// measurer and call encoder in lockstep (equal status at every step, a failing step changes no byte, everything after a failure is dead
/// with <see cref="ABIStatus.InvalidWriterState"/>, finish agrees) under three encode limit sets; a script that completes is then
/// checked exactly like a document, so every state-machine success also proves decode(encode(x)) and encode(decode(y)) identity.</item>
/// <item><see cref="RunSchemaText"/>: raw text. Parses and compiles it as a tuple and as a function signature; a success must be
/// canonical (compiling the canonical text again gives the same text and selector) and its selector must equal the first four bytes of
/// Nethereum's independent Keccak-256 of the canonical signature.</item>
/// </list>
/// Unlike the codec, the targets allocate freely (the oracle and the walks do); only the library calls are measured for allocation, after
/// one warm-up call, and a failure needs every one of three repeats to allocate. This file uses the MSTest assertions of the shared test
/// support (the reference model and the compile helpers), so a harness that links it needs the MSTest packages, as for the RLP target.
/// </summary>
internal static class ABIFuzzTarget
{
    /// <summary>Longest input quoted in full in a failure message.</summary>
    internal const int MaxQuotedBytes = 8192;

    /// <summary>The generous decode limits used for the verdict against the reference model (its own caps are 4,096 elements and 200,000 nodes).</summary>
    internal static readonly ABIDecodeLimits OracleLimits = new(1 << 24, ABIDecodeLimits.HardMaxValueDepth, 4096, 150_000, 10_000_000);

    private static readonly ABIDecodeLimits TightLimits = new(4096, 4, 16, 64, 256);
    private static readonly ABIDecodeLimits MinimalLimits = new(64, 2, 1, 4, 8);

    private static readonly ABIEncodeLimits[] ScriptLimits =
    [
        new(60_000, ABIEncodeLimits.HardMaxValueDepth, 4096, 100_000, 1_000_000),
        new(4096, 16, 64, 512, 4096),
        new(256, 4, 4, 32, 64),
        default,
    ];

    /// <summary>Marks an invariant violation so it can be told apart from an unexpected exception inside the codec.</summary>
    private sealed class FuzzInvariantViolationException(string message) : InvalidOperationException(message);

    // ------------------------------------------------------------------ entry points

    /// <summary>Checks the document target; the first byte selects the schema from <see cref="ABIFuzzSchemas.All"/>.</summary>
    internal static void Run(ReadOnlySpan<byte> input) => Guard(input.ToArray(), static bytes =>
    {
        ABIFuzzSchema schema = bytes.Length == 0 ? ABIFuzzSchemas.All[0] : ABIFuzzSchemas.All[bytes[0] % ABIFuzzSchemas.All.Length];
        CheckDocument(schema, bytes.Length == 0 ? [] : bytes[1..], bytes);
    });

    /// <summary>Checks one document against one schema (the generative tests use schemas outside the registry).</summary>
    internal static void RunDocument(ABIFuzzSchema schema, byte[] document) =>
        Guard(document, bytes => CheckDocument(schema, bytes, bytes));

    /// <summary>Checks the writer state-machine target.</summary>
    internal static void RunWriterScript(ReadOnlySpan<byte> input) => Guard(input.ToArray(), static bytes =>
    {
        ABIFuzzSchema schema = bytes.Length == 0 ? ABIFuzzSchemas.All[0] : ABIFuzzSchemas.All[bytes[0] % ABIFuzzSchemas.All.Length];
        RunScript(schema, ABIFuzzScript.Parse(bytes.Length == 0 ? default : bytes.AsSpan(1)), bytes);
    });

    /// <summary>Checks the same target for events built by a test, quoting <paramref name="quoted"/> on failure.</summary>
    internal static void RunScriptEvents(ABIFuzzSchema schema, List<Ev> events, byte[] quoted) =>
        Guard(quoted, bytes => RunScript(schema, events, bytes));

    /// <summary>Checks the schema parser, compiler and selector target.</summary>
    internal static void RunSchemaText(ReadOnlySpan<byte> input) => Guard(input.ToArray(), static bytes => CheckSchemaText(Encoding.Latin1.GetString(bytes), bytes));

    internal static string Quote(ReadOnlySpan<byte> input) =>
        input.Length <= MaxQuotedBytes
            ? $"0x{Convert.ToHexString(input)}"
            : $"0x{Convert.ToHexString(input[..MaxQuotedBytes])}... ({input.Length} bytes in total)";

    internal static void Guard(byte[] input, Action<byte[]> action)
    {
        try
        {
            action(input);
        }
        catch (FuzzInvariantViolationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Unexpected {exception.GetType().Name} while checking the codec against the oracle (codec or oracle defect): {exception.Message}. Input: {Quote(input)}", exception);
        }
    }

    private static FuzzInvariantViolationException Violation(string message, byte[] quoted) => new($"{message}. Input: {Quote(quoted)}");

    internal static void Expect(bool condition, byte[] quoted, string message)
    {
        if (!condition)
        {
            throw Violation(message, quoted);
        }
    }

    private static void ExpectEqual<T>(T expected, T actual, byte[] quoted, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw Violation($"{message}: expected {expected}, got {actual}", quoted);
        }
    }

    // ------------------------------------------------------------------ allocation probe

    /// <summary>
    /// Runs <paramref name="action"/> once as warm-up (first use of a path can JIT or initialise statics), then up to three measured times;
    /// a real per-call allocation shows on every repeat, a one-off runtime allocation does not.
    /// </summary>
    private static void AssertNoAllocation(string what, Func<long> action, byte[] quoted)
    {
        _ = action();
        long smallest = long.MaxValue;
        for (int attempt = 0; attempt < 3 && smallest != 0; attempt++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            _ = action();
            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Expect(smallest == 0, quoted, $"{what} allocated {smallest} bytes on a repeated identical call");
    }

    // ------------------------------------------------------------------ document target

    private readonly record struct Outcome(
        bool Ok, ABIStatus Status, ABIError Error, bool Valid, bool HasSchema, int EncodedLength, bool PlainOk, ABIStatus PlainStatus);

    private static Outcome Decode(byte[] document, ABISchema schema, ABIDecodeLimits limits)
    {
        bool ok = ABIData.TryDecodeComplete(document, schema, in limits, out ABIData data, out ABIStatus status, out ABIError error);
        bool plainOk = ABIData.TryDecodeComplete(document, schema, in limits, out ABIData plain, out ABIStatus plainStatus);
        // The two views must be the same view.
        bool samePlain = plain.IsValid == data.IsValid && plain.EncodedBytes.Length == data.EncodedBytes.Length;
        return new Outcome(ok, status, error, data.IsValid && samePlain, data.Schema is not null, data.EncodedBytes.Length, plainOk, plainStatus);
    }

    private static bool IsStructuralFailure(ABIStatus status) => status is
        ABIStatus.Truncated or ABIStatus.InvalidOffset or ABIStatus.NonCanonicalLayout or ABIStatus.InvalidPadding or ABIStatus.InvalidScalar
        or ABIStatus.ElementLimitExceeded or ABIStatus.WorkLimitExceeded or ABIStatus.DepthLimitExceeded;

    private static bool IsLimitFailure(ABIStatus status) => status is
        ABIStatus.InputTooLarge or ABIStatus.ElementLimitExceeded or ABIStatus.WorkLimitExceeded or ABIStatus.DepthLimitExceeded;

    private static ABIEncodeLimits ToEncode(ABIDecodeLimits l) =>
        new(l.MaxEncodedBytes, l.MaxValueDepth, l.MaxArrayElements, l.MaxVisitedValues, l.MaxWorkUnits);

    private static byte[] WithSelector(uint selector, byte[] document)
    {
        var call = new byte[document.Length + 4];
        BinaryPrimitives.WriteUInt32BigEndian(call, selector);
        document.CopyTo(call, 4);
        return call;
    }

    private static void CheckDocument(ABIFuzzSchema schema, byte[] document, byte[] quoted)
    {
        byte[] call = WithSelector(schema.Function.Selector, document);
        Outcome oracle = Decode(document, schema.Schema, OracleLimits);

        (string Name, ABIDecodeLimits Limits)[] sets =
        [
            ("default", ABIDecodeLimits.Default),
            ("tight", TightLimits),
            ("minimal", MinimalLimits),
            ("size-1", ABIDecodeLimits.Default with { MaxEncodedBytes = document.Length - 1 }),
            ("zero", default),
            ("oracle", OracleLimits),
        ];
        var outcomes = new Outcome[sets.Length];
        for (int i = 0; i < sets.Length; i++)
        {
            outcomes[i] = CheckLimits(schema, document, call, quoted, sets[i].Name, sets[i].Limits, in oracle);
        }

        // The verdict of the generous set against the independent reference model.
        bool decodes = ABIReference.TryDecodePermissive(schema.Type, document, out RSeq? value);
        bool canonical = decodes && ABIReference.EncodeRoot(schema.Type, value!).AsSpan().SequenceEqual(document);
        if (oracle.Status != ABIStatus.WorkLimitExceeded)
        {
            Expect(oracle.Ok == canonical, quoted,
                $"{schema.Name}: library says {oracle.Status} but the reference model says {(canonical ? "canonical" : decodes ? "decodable but not canonical" : "not decodable")}");
        }

        if (!oracle.Ok)
        {
            CheckFailedView(schema, document, quoted);
            return;
        }

        CheckAccepted(schema, document, value!, quoted);
        for (int i = 0; i < sets.Length; i++)
        {
            CheckEncodeUnderEqualLimits(schema, document, quoted, sets[i].Name, sets[i].Limits, in outcomes[i]);
        }
    }

    private static Outcome CheckLimits(
        ABIFuzzSchema schema, byte[] document, byte[] call, byte[] quoted, string name, ABIDecodeLimits limits, in Outcome oracle)
    {
        Outcome o = Decode(document, schema.Schema, limits);
        string Where() => $"{schema.Name} under {name} {limits}";

        Expect(o.Ok == o.PlainOk && o.Status == o.PlainStatus, quoted, $"{Where()}: the overloads disagree ({o.Status} vs {o.PlainStatus})");
        Expect(o.Ok == (o.Status == ABIStatus.Success), quoted, $"{Where()}: ok {o.Ok} with status {o.Status}");
        if (o.Ok)
        {
            Expect(o.Valid && o.HasSchema && o.EncodedLength == document.Length && o.Error.Status == ABIStatus.Success, quoted, $"{Where()}: accepted view is not the whole document");
            Expect(oracle.Ok, quoted, $"{Where()}: accepted, but the generous limits reject it ({oracle.Status})");
        }
        else
        {
            Expect(!o.Valid && !o.HasSchema && o.EncodedLength == 0, quoted, $"{Where()}: a failed decode left a non-default view");
            Expect(o.Error.Status == o.Status, quoted, $"{Where()}: error status {o.Error.Status} differs from {o.Status}");
            Expect(o.Error.DocumentOffset >= -1 && o.Error.DocumentOffset <= document.Length, quoted, $"{Where()}: offset {o.Error.DocumentOffset} outside the document");
        }

        // Spec 5.2 precedence: invalid limits, then size, then structure.
        if (!limits.IsValid)
        {
            ExpectEqual(ABIStatus.InvalidArgument, o.Status, quoted, $"{Where()}: invalid limits");
        }
        else if (document.Length > limits.MaxEncodedBytes)
        {
            ExpectEqual(ABIStatus.InputTooLarge, o.Status, quoted, $"{Where()}: oversized input");
        }
        else if (!o.Ok)
        {
            Expect(IsStructuralFailure(o.Status), quoted, $"{Where()}: unexpected failure status {o.Status}");
        }

        CheckCallDecode(schema, document, call, quoted, name, limits, o);

        AssertNoAllocation($"{Where()} decode", () => DecodeProbe(document, schema, limits, call), quoted);
        return o;
    }

    private static long DecodeProbe(byte[] document, ABIFuzzSchema schema, ABIDecodeLimits limits, byte[] call)
    {
        long sum = 0;
        if (ABIData.TryDecodeComplete(document, schema.Schema, in limits, out ABIData data, out ABIStatus status, out ABIError error))
        {
            sum += data.EncodedBytes.Length;
        }

        sum += (int)status + error.DocumentOffset;
        if (ABICallData.TryDecode(call, schema.Function, in limits, out ABICallData decoded, out ABIStatus callStatus, out ABIError callError))
        {
            sum += decoded.Selector + decoded.Arguments.EncodedBytes.Length;
            _ = decoded.TryTranslateArgumentRange(new ABIRange(0, document.Length), out ABIRange translated);
            sum += translated.Offset;
        }

        sum += (int)callStatus + callError.DocumentOffset;
        _ = ABICallData.TryDecode(document, schema.Function, in limits, out _, out ABIStatus rawStatus);
        return sum + (int)rawStatus;
    }

    private static void CheckCallDecode(
        ABIFuzzSchema schema, byte[] document, byte[] call, byte[] quoted, string name, ABIDecodeLimits limits, in Outcome documentOutcome)
    {
        string Where() => $"{schema.Name} call under {name} {limits}";
        ABIFunctionSchema function = schema.Function;

        // The call with the right selector: the same validator over the bytes after it.
        bool ok = ABICallData.TryDecode(call, function, in limits, out ABICallData decoded, out ABIStatus status, out ABIError error);
        ABIStatus expected = !limits.IsValid
            ? ABIStatus.InvalidArgument
            : (long)call.Length > limits.MaxEncodedBytes ? ABIStatus.InputTooLarge : documentOutcome.Status;
        ExpectEqual(expected, status, quoted, $"{Where()}: status of the call");
        Expect(ok == (status == ABIStatus.Success) && decoded.IsValid == ok && decoded.Arguments.IsValid == ok, quoted, $"{Where()}: ok/IsValid disagree");
        if (ok)
        {
            Expect(decoded.Selector == function.Selector && decoded.EncodedCall.SequenceEqual(call) && decoded.Arguments.EncodedBytes.SequenceEqual(document),
                quoted, $"{Where()}: accepted call does not expose the selector and the arguments");
            Expect(ReferenceEquals(decoded.Arguments.Schema, function.InputSchema), quoted, $"{Where()}: arguments bound to another schema");

            // Translation only: in-bounds ranges move by four bytes, everything else is refused.
            int n = document.Length;
            Expect(decoded.TryTranslateArgumentRange(new ABIRange(0, n), out ABIRange all) && all == new ABIRange(4, n), quoted, $"{Where()}: translate whole");
            Expect(decoded.TryTranslateArgumentRange(new ABIRange(n, 0), out ABIRange end) && end == new ABIRange(4 + n, 0), quoted, $"{Where()}: translate empty at end");
            foreach (ABIRange bad in new[] { new ABIRange(-1, 0), new ABIRange(0, -1), new ABIRange(n + 1, 0), new ABIRange(0, n + 1), new ABIRange(int.MaxValue, 1), new ABIRange(1, int.MaxValue) })
            {
                Expect(!decoded.TryTranslateArgumentRange(bad, out ABIRange none) && none == default, quoted, $"{Where()}: translated {bad}");
            }
        }
        else
        {
            Expect(decoded.Selector == 0 && decoded.EncodedCall.IsEmpty && !decoded.Arguments.IsValid && decoded.Arguments.Schema is null, quoted, $"{Where()}: failed call left a non-default value");
            Expect(error.Status == status, quoted, $"{Where()}: error status");
            Expect(!decoded.TryTranslateArgumentRange(new ABIRange(0, 0), out ABIRange untouched) && untouched == default, quoted, $"{Where()}: invalid call translated a range");
        }

        // A wrong selector (any one of the four bytes, any one bit) is refused before any argument byte is read, whatever the arguments are.
        for (int selectorByte = 0; selectorByte < 4; selectorByte++)
        {
            byte[] wrong = (byte[])call.Clone();
            wrong[selectorByte] ^= (byte)(1 << (selectorByte * 2));
            _ = ABICallData.TryDecode(wrong, function, in limits, out ABICallData rejected, out ABIStatus wrongStatus);
            ExpectEqual(
                !limits.IsValid ? ABIStatus.InvalidArgument : (long)wrong.Length > limits.MaxEncodedBytes ? ABIStatus.InputTooLarge : ABIStatus.InvalidSelector,
                wrongStatus, quoted, $"{Where()}: wrong selector (byte {selectorByte})");
            Expect(!rejected.IsValid, quoted, $"{Where()}: wrong selector produced a valid call");
        }

        // The document itself taken as a call: its first four bytes must be the selector.
        _ = ABICallData.TryDecode(document, function, in limits, out ABICallData raw, out ABIStatus rawStatus);
        ABIStatus rawExpected;
        if (!limits.IsValid)
        {
            rawExpected = ABIStatus.InvalidArgument;
        }
        else if (document.Length > limits.MaxEncodedBytes)
        {
            rawExpected = ABIStatus.InputTooLarge;
        }
        else if (document.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(document) != function.Selector)
        {
            rawExpected = ABIStatus.InvalidSelector;
        }
        else
        {
            rawExpected = Decode(document[4..], function.InputSchema, limits).Status;
        }

        ExpectEqual(rawExpected, rawStatus, quoted, $"{Where()}: the document taken as a call");
        Expect(raw.IsValid == (rawStatus == ABIStatus.Success), quoted, $"{Where()}: raw call IsValid");
    }

    /// <summary>Every accessor on the default view a failed decode leaves behind: false, <see cref="ABIStatus.InvalidArgument"/>, defaulted outputs.</summary>
    private static void CheckFailedView(ABIFuzzSchema schema, byte[] document, byte[] quoted)
    {
        var limits = OracleLimits;
        int[][] paths = [[], [0], [0, 0], [-1], [int.MaxValue]];
        if (!ABIData.TryDecodeComplete(document, schema.Schema, in limits, out ABIData failed, out _))
        {
            _ = AccessorContracts(in failed, paths, PathMode.InvalidView, schema.Name, quoted);
        }

        ABIData invalid = default;
        _ = AccessorContracts(in invalid, paths, PathMode.InvalidView, schema.Name, quoted);
    }

    // ------------------------------------------------------------------ accepted documents

    private enum PathMode
    {
        /// <summary>The path names a real value; a failing accessor must be a type or range mismatch.</summary>
        ValidPath,

        /// <summary>The path leads nowhere: every accessor must say <see cref="ABIStatus.InvalidPath"/>.</summary>
        InvalidPath,

        /// <summary>The view is not valid: every accessor must say <see cref="ABIStatus.InvalidArgument"/>.</summary>
        InvalidView,
    }

    private static void Contract(bool ok, ABIStatus status, bool outputsDefault, PathMode mode, string accessor, string schema, int[] path, byte[] quoted)
    {
        if (ok != (status == ABIStatus.Success))
        {
            throw Violation($"{schema}: {accessor} at [{string.Join(",", path)}] returned {ok} with status {status}", quoted);
        }

        if (ok)
        {
            if (mode != PathMode.ValidPath)
            {
                throw Violation($"{schema}: {accessor} at [{string.Join(",", path)}] succeeded where it must fail ({mode})", quoted);
            }

            return;
        }

        bool legal = mode switch
        {
            PathMode.InvalidPath => status == ABIStatus.InvalidPath,
            PathMode.InvalidView => status == ABIStatus.InvalidArgument,
            _ => status is ABIStatus.TypeMismatch or ABIStatus.ValueOutOfRange or ABIStatus.InvalidUtf8,
        };
        if (!outputsDefault || !legal)
        {
            throw Violation($"{schema}: {accessor} at [{string.Join(",", path)}] failed with {status} ({mode}); outputs defaulted: {outputsDefault}", quoted);
        }
    }

    /// <summary>
    /// Calls the locator and every typed accessor on every path and enforces the common contract (see <see cref="Contract"/>). Allocation free
    /// unless a violation is thrown; returns a checksum so the calls cannot be optimised away.
    /// </summary>
    private static long AccessorContracts(in ABIData data, int[][] paths, PathMode mode, string schema, byte[] quoted)
    {
        long sum = 0;
        foreach (int[] p in paths)
        {
            bool ok = data.TryLocate(p, out ABIFieldLocation field, out ABIStatus st);
            Contract(ok, st, field.Equals(default(ABIFieldLocation)), mode, "TryLocate", schema, p, quoted);
            sum += field.ValueRange.Length;

            ok = data.TryGetArrayLength(p, out int length, out st);
            Contract(ok, st, length == 0, mode, "TryGetArrayLength", schema, p, quoted);
            sum += length;

            ok = data.TryGetWord(p, out ReadOnlySpan<byte> word, out st);
            Contract(ok, st, word.IsEmpty, mode, "TryGetWord", schema, p, quoted);
            sum += word.Length;

            ok = data.TryGetUInt64(p, out ulong u64, out st);
            Contract(ok, st, u64 == 0, mode, "TryGetUInt64", schema, p, quoted);
            sum += (long)(u64 & 0xff);

            ok = data.TryGetInt64(p, out long i64, out st);
            Contract(ok, st, i64 == 0, mode, "TryGetInt64", schema, p, quoted);
            sum += i64 & 0xff;

            ok = data.TryGetAddress(p, out ReadOnlySpan<byte> address, out st);
            Contract(ok, st, address.IsEmpty, mode, "TryGetAddress", schema, p, quoted);
            sum += address.Length;

            ok = data.TryGetBool(p, out bool flag, out st);
            Contract(ok, st, !flag, mode, "TryGetBool", schema, p, quoted);
            sum += flag ? 1 : 0;

            ok = data.TryGetBytePayload(p, out ReadOnlySpan<byte> payload, out ABIRange payloadRange, out st);
            Contract(ok, st, payload.IsEmpty && payloadRange == default, mode, "TryGetBytePayload", schema, p, quoted);
            sum += payload.Length;

            ok = data.TryGetStringUtf8Validated(p, out ReadOnlySpan<byte> text, out ABIRange textRange, out st);
            Contract(ok, st, text.IsEmpty && textRange == default, mode, "TryGetStringUtf8Validated", schema, p, quoted);
            sum += text.Length;

            ok = data.TryGetFixedBytes(p, out ReadOnlySpan<byte> fixedBytes, out st);
            Contract(ok, st, fixedBytes.IsEmpty, mode, "TryGetFixedBytes", schema, p, quoted);
            sum += fixedBytes.Length;

            ok = data.TryGetFunction(p, out ReadOnlySpan<byte> target, out uint selector, out st);
            Contract(ok, st, target.IsEmpty && selector == 0, mode, "TryGetFunction", schema, p, quoted);
            sum += target.Length + selector;

            ok = data.TryGetSignedScaledWord(p, out ReadOnlySpan<byte> signedScaled, out byte signedScale, out st);
            Contract(ok, st, signedScaled.IsEmpty && signedScale == 0, mode, "TryGetSignedScaledWord", schema, p, quoted);
            sum += signedScaled.Length + signedScale;

            ok = data.TryGetUnsignedScaledWord(p, out ReadOnlySpan<byte> unsignedScaled, out byte unsignedScale, out st);
            Contract(ok, st, unsignedScaled.IsEmpty && unsignedScale == 0, mode, "TryGetUnsignedScaledWord", schema, p, quoted);
            sum += unsignedScaled.Length + unsignedScale;

            ok = data.TryGetUInt256Word(p, out ReadOnlySpan<byte> u256, out st);
            Contract(ok, st, u256.IsEmpty, mode, "TryGetUInt256Word", schema, p, quoted);
            sum += u256.Length;

            ok = data.TryGetInt256Word(p, out ReadOnlySpan<byte> i256, out st);
            Contract(ok, st, i256.IsEmpty, mode, "TryGetInt256Word", schema, p, quoted);
            sum += i256.Length;

            sum += TypedAdapterParity(in data, p, schema, quoted);
        }

        return sum;
    }

    /// <summary>
    /// The typed adapters (<c>ABITypedExtensions</c> readers) must equal the span accessors on every path: same success flag, same status, the value
    /// converted from the same bytes, and a default value on failure. Allocation free; returns a checksum.
    /// </summary>
    private static long TypedAdapterParity(in ABIData data, int[] p, string schema, byte[] quoted)
    {
        long sum = 0;

        void Same(string name, bool spanOk, ABIStatus spanStatus, bool typedOk, ABIStatus typedStatus)
        {
            if (spanOk != typedOk || spanStatus != typedStatus)
            {
                throw Violation($"{schema}: {name} typed ({typedOk}, {typedStatus}) differs from the span accessor ({spanOk}, {spanStatus}) at [{string.Join(",", p)}]", quoted);
            }
        }

        void Value(bool good, string name)
        {
            if (!good)
            {
                throw Violation($"{schema}: {name} value at [{string.Join(",", p)}] is not the converted span value (or not default on failure)", quoted);
            }
        }

        bool spanOk = data.TryGetUInt256Word(p, out ReadOnlySpan<byte> uWord, out ABIStatus spanStatus);
        bool typedOk = data.TryGetUInt256(p, out uint256 u, out ABIStatus typedStatus);
        Same("TryGetUInt256", spanOk, spanStatus, typedOk, typedStatus);
        Value(typedOk ? u.Equals(new uint256(uWord)) : u.IsZero, "TryGetUInt256");
        sum += typedOk ? 1 : 0;

        spanOk = data.TryGetInt256Word(p, out ReadOnlySpan<byte> iWord, out spanStatus);
        typedOk = data.TryGetInt256(p, out int256 i, out typedStatus);
        Same("TryGetInt256", spanOk, spanStatus, typedOk, typedStatus);
        Value(typedOk ? i.Equals(new int256(iWord)) : i.IsZero, "TryGetInt256");
        sum += typedOk ? 1 : 0;

        spanOk = data.TryGetAddress(p, out ReadOnlySpan<byte> address20, out spanStatus);
        typedOk = data.TryGetAddress(p, out Address address, out typedStatus);
        Same("TryGetAddress", spanOk, spanStatus, typedOk, typedStatus);
        Value(typedOk ? address.Equals(Address.FromEvmBytes(address20)) : address.Equals(default(Address)), "TryGetAddress");
        sum += typedOk ? 1 : 0;

        // bytes32 and bytes4 follow the fixed-bytes accessor, plus TypeMismatch for another width.
        bool fixedOk = data.TryGetFixedBytes(p, out ReadOnlySpan<byte> fixedBytes, out ABIStatus fixedStatus);
        typedOk = data.TryGetBytes32(p, out Hash32 hash, out typedStatus);
        Same("TryGetBytes32", fixedOk && fixedBytes.Length == 32, fixedOk ? (fixedBytes.Length == 32 ? ABIStatus.Success : ABIStatus.TypeMismatch) : fixedStatus, typedOk, typedStatus);
        Value(typedOk ? hash.Equals(new Hash32(fixedBytes)) : hash.Equals(default(Hash32)), "TryGetBytes32");
        typedOk = data.TryGetBytes4(p, out FunctionSelector four, out typedStatus);
        Same("TryGetBytes4", fixedOk && fixedBytes.Length == 4, fixedOk ? (fixedBytes.Length == 4 ? ABIStatus.Success : ABIStatus.TypeMismatch) : fixedStatus, typedOk, typedStatus);
        Value(typedOk ? four.Equals(new FunctionSelector(fixedBytes)) : four.Equals(default(FunctionSelector)), "TryGetBytes4");
        sum += typedOk ? 1 : 0;

        spanOk = data.TryGetFunction(p, out ReadOnlySpan<byte> target20, out uint rawSelector, out spanStatus);
        typedOk = data.TryGetFunction(p, out Address target, out FunctionSelector selector, out typedStatus);
        Same("TryGetFunction", spanOk, spanStatus, typedOk, typedStatus);
        Value(typedOk ? target.Equals(Address.FromEvmBytes(target20)) && selector.Value == rawSelector : target.Equals(default(Address)) && selector.IsZero, "TryGetFunction");
        sum += typedOk ? 1 : 0;

        return sum;
    }

    private const int MaxSweptPaths = 256;

    private static int[][] InvalidPathsOf(List<RLocation> locations)
    {
        var paths = new List<int[]>
        {
            new int[40], // longer than any schema is deep: ends in a primitive or an empty tuple
            new[] { -1 },
            new[] { int.MaxValue },
            new[] { int.MinValue },
        };
        foreach (RLocation location in locations.Take(MaxSweptPaths / 4))
        {
            int[] p = location.Path;
            paths.Add([.. p, -1]);
            paths.Add([.. p, int.MaxValue]);
            switch (location.Type)
            {
                case RTupleType tuple:
                    paths.Add([.. p, tuple.Items.Length]);
                    break;
                case RFixedArrayType fixedArray:
                    paths.Add([.. p, fixedArray.Length]);
                    break;
                case RDynamicArrayType:
                    paths.Add([.. p, ((RSeq)location.Value).Items.Length]);
                    break;
                default:
                    paths.Add([.. p, 0]); // into a primitive or a bytes/string leaf
                    break;
            }
        }

        return [.. paths];
    }

    private static void CheckAccepted(ABIFuzzSchema schema, byte[] document, RSeq value, byte[] quoted)
    {
        var locations = new List<RLocation>();
        ABIReference.EncodeRoot(schema.Type, value, locations);
        int[][] validPaths = locations.Take(MaxSweptPaths).Select(l => l.Path).ToArray();
        int[][] invalidPaths = InvalidPathsOf(locations);
        var limits = OracleLimits;
        Expect(ABIData.TryDecodeComplete(document, schema.Schema, in limits, out ABIData data, out ABIStatus status), quoted, $"decode flipped to {status}");

        // Every location, range and typed value against the reference encoder's own bookkeeping.
        ABIReferenceAssertions.AssertEveryPathMatches(in data, schema.Type, value, document);
        _ = AccessorContracts(in data, validPaths, PathMode.ValidPath, schema.Name, quoted);
        _ = AccessorContracts(in data, invalidPaths, PathMode.InvalidPath, schema.Name, quoted);

        AssertNoAllocation($"{schema.Name} locator and accessors", () =>
        {
            ABIDecodeLimits l = OracleLimits;
            _ = ABIData.TryDecodeComplete(document, schema.Schema, in l, out ABIData d, out _);
            return AccessorContracts(in d, validPaths, PathMode.ValidPath, schema.Name, quoted) + AccessorContracts(in d, invalidPaths, PathMode.InvalidPath, schema.Name, quoted)
                + CompareProbe(document);
        }, quoted);

        CheckAccessorKindMatrix(in data, locations, schema.Name, quoted);
        CheckComparisons(document, quoted);
        CheckRoundTrip(schema, document, in data, quoted);
    }

    /// <summary>Both comparison helpers over the first two words of the document (allocation probe only; the values are checked against BigInteger in <see cref="CheckComparisons"/>).</summary>
    private static long CompareProbe(byte[] document)
    {
        if (document.Length < 64)
        {
            return 0;
        }

        _ = ABIData.TryCompareUnsigned256(document.AsSpan(0, 32), document.AsSpan(32, 32), out int unsigned, out _);
        _ = ABIData.TryCompareSigned256(document.AsSpan(0, 32), document.AsSpan(32, 32), out int signed, out _);
        return unsigned + signed;
    }

    /// <summary>
    /// Every accessor on every swept location against the kind it serves: a mismatched kind must fail with <see cref="ABIStatus.TypeMismatch"/> (never succeed, never
    /// reinterpret), a matching kind may fail only with the documented narrowing or text status.
    /// </summary>
    private static void CheckAccessorKindMatrix(in ABIData data, List<RLocation> locations, string schemaName, byte[] quoted)
    {
        foreach (RLocation location in locations.Take(MaxSweptPaths))
        {
            int[] p = location.Path;
            ABITypeKind kind = ABIReferenceAssertions.KindOf(location.Type);
            bool elementary = location.Type is RElementary;

            void Check(string accessor, bool kindMatches, bool ok, ABIStatus status)
            {
                bool legal = kindMatches
                    ? ok || status is ABIStatus.ValueOutOfRange or ABIStatus.InvalidUtf8
                    : !ok && status == ABIStatus.TypeMismatch;
                if (!legal)
                {
                    throw Violation($"{schemaName}: {accessor} on a {kind} at [{string.Join(",", p)}] returned {ok} with {status}", quoted);
                }
            }

            Check("TryGetWord", elementary && kind is not (ABITypeKind.Fixed or ABITypeKind.UFixed), data.TryGetWord(p, out _, out ABIStatus st), st);
            Check("TryGetUInt64", kind == ABITypeKind.UInt, data.TryGetUInt64(p, out _, out st), st);
            Check("TryGetInt64", kind == ABITypeKind.Int, data.TryGetInt64(p, out _, out st), st);
            Check("TryGetUInt256Word", kind == ABITypeKind.UInt, data.TryGetUInt256Word(p, out _, out st), st);
            Check("TryGetInt256Word", kind == ABITypeKind.Int, data.TryGetInt256Word(p, out _, out st), st);
            Check("TryGetAddress", kind == ABITypeKind.Address, data.TryGetAddress(p, out _, out st), st);
            Check("TryGetBool", kind == ABITypeKind.Bool, data.TryGetBool(p, out _, out st), st);
            Check("TryGetFixedBytes", kind == ABITypeKind.FixedBytes, data.TryGetFixedBytes(p, out _, out st), st);
            Check("TryGetFunction", kind == ABITypeKind.Function, data.TryGetFunction(p, out _, out _, out st), st);
            Check("TryGetSignedScaledWord", kind == ABITypeKind.Fixed, data.TryGetSignedScaledWord(p, out _, out _, out st), st);
            Check("TryGetUnsignedScaledWord", kind == ABITypeKind.UFixed, data.TryGetUnsignedScaledWord(p, out _, out _, out st), st);
            Check("TryGetBytePayload", kind is ABITypeKind.Bytes or ABITypeKind.String, data.TryGetBytePayload(p, out _, out _, out st), st);
            Check("TryGetStringUtf8Validated", kind == ABITypeKind.String, data.TryGetStringUtf8Validated(p, out _, out _, out st), st);
            Check("TryGetArrayLength", kind is ABITypeKind.FixedArray or ABITypeKind.DynamicArray, data.TryGetArrayLength(p, out _, out st), st);
        }
    }

    /// <summary>The 256-bit comparison helpers against BigInteger on words taken from the document (and a wrong length).</summary>
    private static void CheckComparisons(byte[] document, byte[] quoted)
    {
        int words = Math.Min(document.Length / 32, 6);
        for (int a = 0; a < words; a++)
        {
            for (int b = 0; b < words; b++)
            {
                ReadOnlySpan<byte> left = document.AsSpan(a * 32, 32);
                ReadOnlySpan<byte> right = document.AsSpan(b * 32, 32);
                Expect(ABIData.TryCompareUnsigned256(left, right, out int unsigned, out ABIStatus s1) && s1 == ABIStatus.Success
                    && unsigned == BigInteger.Compare(ABIReference.Unsigned(left), ABIReference.Unsigned(right)), quoted, "TryCompareUnsigned256 disagrees with BigInteger");
                Expect(ABIData.TryCompareSigned256(left, right, out int signed, out ABIStatus s2) && s2 == ABIStatus.Success
                    && signed == BigInteger.Compare(ABIReference.Signed(left), ABIReference.Signed(right)), quoted, "TryCompareSigned256 disagrees with BigInteger");
            }
        }

        Expect(!ABIData.TryCompareUnsigned256(document.AsSpan(0, Math.Min(31, document.Length)), new byte[32], out int none, out ABIStatus bad) && none == 0 && bad == ABIStatus.InvalidArgument,
            quoted, "TryCompareUnsigned256 accepted a short word");
        Expect(!ABIData.TryCompareSigned256(new byte[33], new byte[32], out none, out bad) && none == 0 && bad == ABIStatus.InvalidArgument,
            quoted, "TryCompareSigned256 accepted a long word");
    }

    // ------------------------------------------------------------------ re-encoding

    private static void Need(bool ok, ABIStatus status, string accessor, int[] path, byte[] quoted) =>
        Expect(ok && status == ABIStatus.Success, quoted, $"{accessor} at [{string.Join(",", path)}] failed with {status} on an accepted document");

    /// <summary>Reads one value through the public accessors and appends the writer events that reproduce it.</summary>
    private static void EmitFromView(in ABIData data, RType type, List<int> path, List<Ev> events, List<int> stringEvents, byte[] quoted)
    {
        int[] p = [.. path];
        ABIStatus st;
        switch (type)
        {
            case RElementary e:
                switch (e.Kind)
                {
                    case ABITypeKind.UInt:
                        Need(data.TryGetUInt256Word(p, out var u, out st), st, "TryGetUInt256Word", p, quoted);
                        events.Add(Ev.Uint256(u.ToArray()));
                        break;
                    case ABITypeKind.Int:
                        Need(data.TryGetInt256Word(p, out var i, out st), st, "TryGetInt256Word", p, quoted);
                        events.Add(Ev.Int256(i.ToArray()));
                        break;
                    case ABITypeKind.Address:
                        Need(data.TryGetAddress(p, out var a, out st), st, "TryGetAddress", p, quoted);
                        events.Add(Ev.Addr(a.ToArray()));
                        break;
                    case ABITypeKind.Bool:
                        Need(data.TryGetBool(p, out bool b, out st), st, "TryGetBool", p, quoted);
                        events.Add(Ev.Bool(b));
                        break;
                    case ABITypeKind.FixedBytes:
                        Need(data.TryGetFixedBytes(p, out var f, out st), st, "TryGetFixedBytes", p, quoted);
                        events.Add(Ev.FixedB(f.ToArray()));
                        break;
                    case ABITypeKind.Function:
                        Need(data.TryGetFunction(p, out var target, out uint selector, out st), st, "TryGetFunction", p, quoted);
                        events.Add(Ev.Func(target.ToArray(), selector));
                        break;
                    case ABITypeKind.Fixed:
                        Need(data.TryGetSignedScaledWord(p, out var sw, out byte ss, out st), st, "TryGetSignedScaledWord", p, quoted);
                        ExpectEqual((byte)e.Scale, ss, quoted, "signed scale");
                        events.Add(new Ev(EvKind.SignedScaled, sw.ToArray()));
                        break;
                    default:
                        Need(data.TryGetUnsignedScaledWord(p, out var uw, out byte us, out st), st, "TryGetUnsignedScaledWord", p, quoted);
                        ExpectEqual((byte)e.Scale, us, quoted, "unsigned scale");
                        events.Add(new Ev(EvKind.UnsignedScaled, uw.ToArray()));
                        break;
                }

                break;

            case RBlobType blob:
                Need(data.TryGetBytePayload(p, out var payload, out _, out st), st, "TryGetBytePayload", p, quoted);
                if (blob.IsString)
                {
                    stringEvents.Add(events.Count);
                }

                events.Add(Ev.Blob(payload.ToArray())); // raw: a string's on-chain bytes need not be UTF-8
                break;

            case RTupleType tuple:
                events.Add(Ev.Begin("tuple"));
                EmitChildren(in data, tuple.Items, path, events, stringEvents, quoted);
                events.Add(Ev.End);
                break;

            case RFixedArrayType fixedArray:
                Need(data.TryGetArrayLength(p, out int fixedLength, out st), st, "TryGetArrayLength", p, quoted);
                ExpectEqual(fixedArray.Length, fixedLength, quoted, "fixed array length");
                events.Add(Ev.Begin("fixed"));
                EmitChildren(in data, Enumerable.Repeat(fixedArray.Element, fixedLength).ToArray(), path, events, stringEvents, quoted);
                events.Add(Ev.End);
                break;

            case RDynamicArrayType dynamicArray:
                Need(data.TryGetArrayLength(p, out int count, out st), st, "TryGetArrayLength", p, quoted);
                events.Add(Ev.Begin("dynamic", count));
                EmitChildren(in data, Enumerable.Repeat(dynamicArray.Element, count).ToArray(), path, events, stringEvents, quoted);
                events.Add(Ev.End);
                break;
        }
    }

    private static void EmitChildren(in ABIData data, RType[] types, List<int> path, List<Ev> events, List<int> stringEvents, byte[] quoted)
    {
        for (int i = 0; i < types.Length; i++)
        {
            path.Add(i);
            EmitFromView(in data, types[i], path, events, stringEvents, quoted);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static void CheckRoundTrip(ABIFuzzSchema schema, byte[] document, in ABIData data, byte[] quoted)
    {
        var events = new List<Ev>();
        var stringEvents = new List<int>();
        var path = new List<int>();
        EmitChildren(in data, schema.Type.Items, path, events, stringEvents, quoted);

        ABIEncodeLimits encode = ToEncode(OracleLimits);

        // Strings through both entry points: identical bytes when every string is valid UTF-8; otherwise InvalidUtf8 (and nothing else) at
        // the first string that is not, from TryWriteBytes and TryWriteStringUtf8 alike (no write path emits a string that is not text).
        var text = new List<Ev>(events);
        int firstInvalid = -1;
        foreach (int index in stringEvents)
        {
            text[index] = Ev.Text(events[index].Data!);
            if (firstInvalid < 0 && !Utf8.IsValid(events[index].Data!))
            {
                firstInvalid = index;
            }
        }

        if (firstInvalid >= 0)
        {
            foreach ((List<Ev> script, string method) in new[] { (events, "TryWriteBytes"), (text, "TryWriteStringUtf8") })
            {
                RunResult w = ABIWriteTestSupport.RunWriter(script, schema.Schema, new byte[document.Length + 64], encode);
                RunResult m = ABIWriteTestSupport.RunMeasurer(script, schema.Schema, encode);
                Expect(!w.Ok && !m.Ok && w.Status == ABIStatus.InvalidUtf8 && m.Status == ABIStatus.InvalidUtf8 && w.FailedAt == firstInvalid && m.FailedAt == firstInvalid, quoted,
                    $"invalid UTF-8 through {method}: writer {w}, measurer {m}");
            }

            return; // the document decodes (structure only) but cannot be re-encoded, so the budget checks below have nothing to measure
        }

        CheckEncodes(schema, events, document, encode, quoted, "oracle limits");
        CheckEncodes(schema, text, document, encode, quoted, "validated strings");

        if (document.Length > 0)
        {
            // One byte over the budget or short of destination: refused identically, and no valid result.
            ABIEncodeLimits shortBudget = encode with { MaxEncodedBytes = document.Length - 1 };
            RunResult w = ABIWriteTestSupport.RunWriter(events, schema.Schema, new byte[document.Length + 64], shortBudget);
            RunResult m = ABIWriteTestSupport.RunMeasurer(events, schema.Schema, shortBudget);
            Expect(!w.Ok && !m.Ok && w.Status == ABIStatus.InputTooLarge && m.Status == ABIStatus.InputTooLarge, quoted, $"MaxEncodedBytes = size - 1: writer {w}, measurer {m}");

            w = ABIWriteTestSupport.RunWriter(events, schema.Schema, new byte[document.Length - 1], encode);
            Expect(!w.Ok && w.Status == ABIStatus.InsufficientDestination, quoted, $"destination one byte short: {w}");
        }
    }

    private static void CheckEncodes(ABIFuzzSchema schema, List<Ev> events, byte[] document, ABIEncodeLimits limits, byte[] quoted, string label)
    {
        RunResult measured = ABIWriteTestSupport.RunMeasurer(events, schema.Schema, limits);
        Expect(measured.Ok && measured.Length == document.Length, quoted, $"{label}: measurer {measured}, document is {document.Length} bytes");

        var buffer = new byte[document.Length + 64];
        Array.Fill(buffer, ABIWriteTestSupport.Garbage);
        RunResult written = ABIWriteTestSupport.RunWriter(events, schema.Schema, buffer, limits);
        Expect(written.Ok && written.Length == document.Length, quoted, $"{label}: writer {written}, document is {document.Length} bytes");
        Expect(buffer.AsSpan(0, document.Length).SequenceEqual(document), quoted, $"{label}: re-encoded bytes differ from the document");
        Expect(buffer.AsSpan(document.Length).IndexOfAnyExcept(ABIWriteTestSupport.Garbage) < 0, quoted, $"{label}: the writer touched bytes past the document");

        var callBuffer = new byte[document.Length + 4 + 64];
        Array.Fill(callBuffer, ABIWriteTestSupport.Garbage);
        ABIEncodeLimits callLimits = limits with { MaxEncodedBytes = limits.MaxEncodedBytes + 4 };
        RunResult call = ABIWriteTestSupport.RunCall(events, schema.Function, callBuffer, callLimits);
        Expect(call.Ok && call.Length == document.Length + 4, quoted, $"{label}: call encoder {call}");
        Expect(BinaryPrimitives.ReadUInt32BigEndian(callBuffer) == schema.Function.Selector && callBuffer.AsSpan(4, document.Length).SequenceEqual(document), quoted, $"{label}: call bytes are not selector + document");
        Expect(callBuffer.AsSpan(document.Length + 4).IndexOfAnyExcept(ABIWriteTestSupport.Garbage) < 0, quoted, $"{label}: the call encoder touched bytes past the call");

        // Zero allocation on the success path of all three encoders, after one warm-up.
        AssertNoAllocation($"{schema.Name} {label} writer", () => ABIWriteTestSupport.RunWriter(events, schema.Schema, buffer, limits).Length, quoted);
        AssertNoAllocation($"{schema.Name} {label} measurer", () => ABIWriteTestSupport.RunMeasurer(events, schema.Schema, limits).Length, quoted);
        AssertNoAllocation($"{schema.Name} {label} call encoder", () => ABIWriteTestSupport.RunCall(events, schema.Function, callBuffer, callLimits).Length, quoted);
    }

    /// <summary>A document that decodes under L must write under the equal encode limits; one rejected for a limit must not write.</summary>
    private static void CheckEncodeUnderEqualLimits(ABIFuzzSchema schema, byte[] document, byte[] quoted, string name, ABIDecodeLimits limits, in Outcome outcome)
    {
        if (!limits.IsValid)
        {
            return;
        }

        var events = new List<Ev>();
        var limitsForRead = OracleLimits;
        Expect(ABIData.TryDecodeComplete(document, schema.Schema, in limitsForRead, out ABIData data, out _), quoted, "a document accepted a moment ago is rejected");
        var stringEvents = new List<int>();
        EmitChildren(in data, schema.Type.Items, [], events, stringEvents, quoted);
        if (stringEvents.Any(index => !Utf8.IsValid(events[index].Data!)))
        {
            return; // a string that is not UTF-8 decodes but is never re-encoded (CheckRoundTrip asserts the refusal)
        }

        ABIEncodeLimits encode = ToEncode(limits);
        RunResult measured = ABIWriteTestSupport.RunMeasurer(events, schema.Schema, encode);
        RunResult written = ABIWriteTestSupport.RunWriter(events, schema.Schema, new byte[Math.Min(document.Length, limits.MaxEncodedBytes) + 64], encode);
        string where = $"{schema.Name}: under {name} {limits} the decoder says {outcome.Status}";
        if (outcome.Ok)
        {
            Expect(measured.Ok && written.Ok && measured.Length == document.Length && written.Length == document.Length, quoted, $"{where} but the equal encode limits give measurer {measured}, writer {written}");
        }
        else if (IsLimitFailure(outcome.Status))
        {
            Expect(!measured.Ok && !written.Ok && measured.Status == written.Status && IsLimitFailure(written.Status), quoted, $"{where} but the equal encode limits give measurer {measured}, writer {written}");
        }
    }

    // ------------------------------------------------------------------ writer script target

    private static void RunScript(ABIFuzzSchema schema, List<Ev> events, byte[] quoted)
    {
        foreach (ABIEncodeLimits limits in ScriptLimits)
        {
            Lockstep(schema, events, limits, quoted);
        }
    }

    private static void Lockstep(ABIFuzzSchema schema, List<Ev> events, ABIEncodeLimits limits, byte[] quoted)
    {
        string Where() => $"{schema.Name} script under {limits}";
        int room = limits.IsValid ? limits.MaxEncodedBytes : 0;
        var writerDestination = new byte[room + 64];
        var callDestination = new byte[room + 68];
        Array.Fill(writerDestination, ABIWriteTestSupport.Garbage);
        Array.Fill(callDestination, ABIWriteTestSupport.Garbage);
        var writerFrames = new ABIWriteFrame[ABIEncodeLimits.HardMaxValueDepth];
        var measureFrames = new ABIMeasureFrame[ABIEncodeLimits.HardMaxValueDepth];
        var callFrames = new ABIWriteFrame[ABIEncodeLimits.HardMaxValueDepth];
        ABIEncodeLimits callLimits = limits with { MaxEncodedBytes = limits.MaxEncodedBytes + 4 };

        bool wb = ABIWriter.TryBegin(schema.Schema, writerDestination, writerFrames, in limits, out ABIWriter w, out ABIStatus ws);
        bool mb = ABIMeasurer.TryBegin(schema.Schema, measureFrames, in limits, out ABIMeasurer m, out ABIStatus ms);
        bool cb = ABICallEncoder.TryBegin(schema.Function, callDestination, callFrames, in callLimits, out ABICallEncoder c, out ABIStatus cs);
        Expect(wb == mb && mb == cb && ws == ms && ms == cs, quoted, $"{Where()}: TryBegin disagrees: writer {ws}, measurer {ms}, call encoder {cs}");
        if (!wb)
        {
            Expect(ws != ABIStatus.Success, quoted, $"{Where()}: TryBegin failed with Success");
            if (!limits.IsValid)
            {
                ExpectEqual(ABIStatus.InvalidArgument, ws, quoted, $"{Where()}: invalid limits");
            }

            Expect(writerDestination.AsSpan().IndexOfAnyExcept(ABIWriteTestSupport.Garbage) < 0 && callDestination.AsSpan().IndexOfAnyExcept(ABIWriteTestSupport.Garbage) < 0, quoted, $"{Where()}: a failed TryBegin wrote bytes");
            return;
        }

        var snapshot = new byte[writerDestination.Length];
        bool dead = false;
        for (int i = 0; i < events.Count; i++)
        {
            Ev ev = events[i];
            writerDestination.CopyTo(snapshot, 0);
            bool a = ABIWriteTestSupport.Apply(ref w, in ev, out ABIStatus sa);
            bool b = ABIWriteTestSupport.Apply(ref m, in ev, out ABIStatus sb);
            bool d = ABIWriteTestSupport.Apply(ref c, in ev, out ABIStatus sc);
            Expect(a == b && b == d && sa == sb && sb == sc, quoted, $"{Where()}: event {i} {ev.Kind} gives writer {sa}, measurer {sb}, call encoder {sc}");
            Expect(a == (sa == ABIStatus.Success), quoted, $"{Where()}: event {i} returned {a} with {sa}");
            if (dead)
            {
                ExpectEqual(ABIStatus.InvalidWriterState, sa, quoted, $"{Where()}: event {i} after a failure");
            }

            if (!a)
            {
                dead = true;
                Expect(writerDestination.AsSpan().SequenceEqual(snapshot), quoted, $"{Where()}: failing event {i} {ev.Kind} ({sa}) changed the destination");
            }
        }

        bool fa = w.TryFinish(out int writtenLength, out ABIStatus fsa);
        bool fb = m.TryFinish(out int measuredLength, out ABIStatus fsb);
        bool fc = c.TryFinish(out int callLength, out ABIStatus fsc);
        Expect(fa == fb && fb == fc && fsa == fsb && fsb == fsc, quoted, $"{Where()}: TryFinish disagrees: writer {fsa}, measurer {fsb}, call encoder {fsc}");
        Expect(fa == (fsa == ABIStatus.Success), quoted, $"{Where()}: TryFinish returned {fa} with {fsa}");

        if (!fa)
        {
            Expect(writtenLength == 0 && measuredLength == 0 && callLength == 0, quoted, $"{Where()}: a failed finish reported a length");
        }
        else
        {
            Expect(!dead, quoted, $"{Where()}: finished after a failed event");
            ExpectEqual(measuredLength, writtenLength, quoted, $"{Where()}: measured vs written length");
            ExpectEqual(writtenLength + 4, callLength, quoted, $"{Where()}: call length");
            Expect(writerDestination.AsSpan(writtenLength).IndexOfAnyExcept(ABIWriteTestSupport.Garbage) < 0, quoted, $"{Where()}: bytes past the document were touched");
            Expect(callDestination.AsSpan(callLength).IndexOfAnyExcept(ABIWriteTestSupport.Garbage) < 0, quoted, $"{Where()}: bytes past the call were touched");
            byte[] document = writerDestination[..writtenLength];
            Expect(BinaryPrimitives.ReadUInt32BigEndian(callDestination) == schema.Function.Selector && callDestination.AsSpan(4, writtenLength).SequenceEqual(document), quoted, $"{Where()}: call bytes");

            // Strict decode under the equal budgets (spec 7.7 invariant 5), then everything a received document must satisfy.
            var equal = new ABIDecodeLimits(limits.MaxEncodedBytes, limits.MaxValueDepth, limits.MaxArrayElements, limits.MaxWrittenValues, limits.MaxWorkUnits);
            Outcome decoded = Decode(document, schema.Schema, equal);
            Expect(decoded.Ok, quoted, $"{Where()}: the written document does not decode under the equal decode limits ({decoded.Status})");
            CheckDocument(schema, document, quoted);
        }

        // A second finish, and any further mutation, fail on all three.
        Expect(!w.TryFinish(out _, out _) && !m.TryFinish(out _, out _) && !c.TryFinish(out _, out _), quoted, $"{Where()}: a second TryFinish succeeded");
        Expect(!w.TryBeginTuple(out ABIStatus after) && after == ABIStatus.InvalidWriterState, quoted, $"{Where()}: a finished writer accepted a mutation ({after})");

        // Zero allocation of the whole replay, success or failure, after one warm-up.
        AssertNoAllocation($"{Where()} replay", () => ScriptReplay(schema, events, limits, callLimits, writerDestination, callDestination, writerFrames, measureFrames, callFrames), quoted);
    }

    private static long ScriptReplay(
        ABIFuzzSchema schema, List<Ev> events, ABIEncodeLimits limits, ABIEncodeLimits callLimits,
        byte[] writerDestination, byte[] callDestination, ABIWriteFrame[] writerFrames, ABIMeasureFrame[] measureFrames, ABIWriteFrame[] callFrames)
    {
        long sum = 0;
        if (ABIWriter.TryBegin(schema.Schema, writerDestination, writerFrames, in limits, out ABIWriter w, out _))
        {
            for (int i = 0; i < events.Count; i++)
            {
                _ = ABIWriteTestSupport.Apply(ref w, events[i], out ABIStatus s);
                sum += (int)s;
            }

            _ = w.TryFinish(out int length, out _);
            sum += length;
        }

        if (ABIMeasurer.TryBegin(schema.Schema, measureFrames, in limits, out ABIMeasurer m, out _))
        {
            for (int i = 0; i < events.Count; i++)
            {
                _ = ABIWriteTestSupport.Apply(ref m, events[i], out ABIStatus s);
                sum += (int)s;
            }

            _ = m.TryFinish(out int length, out _);
            sum += length;
        }

        if (ABICallEncoder.TryBegin(schema.Function, callDestination, callFrames, in callLimits, out ABICallEncoder c, out _))
        {
            for (int i = 0; i < events.Count; i++)
            {
                _ = ABIWriteTestSupport.Apply(ref c, events[i], out ABIStatus s);
                sum += (int)s;
            }

            _ = c.TryFinish(out int length, out _);
            sum += length;
        }

        return sum;
    }

    // ------------------------------------------------------------------ schema text target

    private static void CheckSchemaText(string text, byte[] quoted)
    {
        var limits = ABICompileLimits.Default;

        bool tuple = ABITupleDefinition.TryParse(text, out ABITupleDefinition? definition, out ABISchemaError parseError);
        Expect(tuple == (definition is not null), quoted, "TryParse: result and out value disagree");
        if (tuple)
        {
            Expect(ABISchemaCompiler.TryCompileArguments(definition!, in limits, out ABISchema? schema, out ABISchemaError error) == (schema is not null), quoted, "TryCompileArguments: result and out value disagree");
            if (schema is not null)
            {
                Expect(error.Status == ABIStatus.Success, quoted, "TryCompileArguments succeeded with a failing error");
                CheckCanonicalTuple(schema.CanonicalType, schema, limits, quoted);
            }
            else
            {
                Expect(error.Status != ABIStatus.Success && error.Reason != ABISchemaErrorReason.None, quoted, $"TryCompileArguments failed without a status or reason: {error}");
            }

            // Tight and invalid limits never throw either.
            var tight = new ABICompileLimits(8, 3, 4, 4, 256);
            _ = ABISchemaCompiler.TryCompileArguments(definition!, in tight, out _, out _);
            var zero = default(ABICompileLimits);
            Expect(!ABISchemaCompiler.TryCompileArguments(definition!, in zero, out ABISchema? none, out ABISchemaError invalid) && none is null && invalid.Status == ABIStatus.InvalidArgument, quoted, "invalid compile limits");
        }
        else
        {
            Expect(parseError.Status != ABIStatus.Success, quoted, "TryParse failed with Success");
        }

        bool function = ABISchemaCompiler.TryCompileFunctionSignature(text, in limits, out ABIFunctionSchema? compiled, out ABISchemaError functionError);
        Expect(function == (compiled is not null), quoted, "TryCompileFunctionSignature: result and out value disagree");
        if (compiled is null)
        {
            Expect(functionError.Status != ABIStatus.Success && functionError.Reason != ABISchemaErrorReason.None, quoted, $"TryCompileFunctionSignature failed without a status or reason: {functionError}");
            return;
        }

        // The selector is the first four bytes of an independent Keccak-256 of the canonical signature.
        byte[] hash = Nethereum.Util.Sha3Keccack.Current.CalculateHash(Encoding.ASCII.GetBytes(compiled.CanonicalSignature));
        ExpectEqual(BinaryPrimitives.ReadUInt32BigEndian(hash), compiled.Selector, quoted, $"selector of {compiled.CanonicalSignature}");
        Expect(compiled.CanonicalSignature.EndsWith(compiled.InputSchema.CanonicalType, StringComparison.Ordinal)
            && compiled.CanonicalSignature.StartsWith(compiled.Name, StringComparison.Ordinal), quoted, "canonical signature is not name + canonical type");

        // Canonical text is a fixed point.
        Expect(ABISchemaCompiler.TryCompileFunctionSignature(compiled.CanonicalSignature, in limits, out ABIFunctionSchema? again, out ABISchemaError againError)
            && again is not null && again.CanonicalSignature == compiled.CanonicalSignature && again.Selector == compiled.Selector, quoted,
            $"canonical signature {compiled.CanonicalSignature} does not compile to itself ({againError})");
    }

    private static void CheckCanonicalTuple(string canonical, ABISchema schema, ABICompileLimits limits, byte[] quoted)
    {
        Expect(ABITupleDefinition.TryParse(canonical, out ABITupleDefinition? reparsed, out _)
            && ABISchemaCompiler.TryCompileArguments(reparsed, in limits, out ABISchema? again, out _)
            && again is not null && again.CanonicalType == canonical && again.ArgumentCount == schema.ArgumentCount && again.IsDynamic == schema.IsDynamic, quoted,
            $"canonical tuple {canonical} does not compile to itself");
    }
}
