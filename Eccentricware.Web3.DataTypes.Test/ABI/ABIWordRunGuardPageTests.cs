using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Real over-read tests (SIMD guidance 11.3: "use constrained/guard-page tests or equivalent harnesses to detect overreads; avoid assuming that a successful span
/// slice makes an unsafe load safe"). The vector kernels of <see cref="ABIWordRun"/> loaded with <c>Vector128/256.LoadUnsafe</c> until 2026-11-19 and now load
/// bounds-checked slices (design notes row 85); these tests stay as the backstop that no load reaches past its input, whatever form the kernels take.
/// Here each input is placed so that its LAST byte is the last byte of a mapped page and the next page is <c>PROT_NONE</c>: a load of even one byte past the span
/// raises SIGSEGV and kills the test host, which fails the run (a crash, not an assertion: there is no way to catch an access violation in managed code). A managed-memory
/// poisoning test cannot do this: the bytes after a managed array are readable, so an over-read there is silent.
/// Linux only (mmap and mprotect through libc); on any other platform every test is inconclusive with the reason stated.
/// </summary>
[TestClass]
public sealed class ABIWordRunGuardPageTests
{
    private const int ProtRead = 1;
    private const int ProtWrite = 2;
    private const int ProtNone = 0;
    private const int MapPrivate = 0x02;
    private const int MapAnonymous = 0x20; // Linux x86-64 and arm64

    [DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
    private static extern nint Mmap(nint address, nuint length, int protection, int flags, int fd, nint offset);

    [DllImport("libc", EntryPoint = "mprotect", SetLastError = true)]
    private static extern int Mprotect(nint address, nuint length, int protection);

    [DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
    private static extern int Munmap(nint address, nuint length);

    /// <summary>Two pages: the first read/write, the second PROT_NONE. <see cref="Span"/> hands out windows that END at the last byte of the first page.</summary>
    private sealed class GuardedPages : IDisposable
    {
        private readonly nint _base;
        private readonly int _page;

        internal GuardedPages()
        {
            _page = Environment.SystemPageSize;
            _base = Mmap(0, (nuint)(2 * _page), ProtRead | ProtWrite, MapPrivate | MapAnonymous, -1, 0);
            if (_base == -1 || _base == 0)
            {
                throw new InvalidOperationException($"mmap failed (errno {Marshal.GetLastWin32Error()})");
            }

            if (Mprotect(_base + _page, (nuint)_page, ProtNone) != 0)
            {
                throw new InvalidOperationException($"mprotect failed (errno {Marshal.GetLastWin32Error()})");
            }
        }

        /// <summary>A writable span of <paramref name="length"/> bytes whose last byte is the last byte before the guard page.</summary>
        internal Span<byte> Span(int length)
        {
            Assert.IsTrue(length >= 0 && length <= _page);
            ref byte first = ref Unsafe.AddByteOffset(ref Unsafe.NullRef<byte>(), _base + _page - length);
            return MemoryMarshal.CreateSpan(ref first, length);
        }

        public void Dispose() => Munmap(_base, (nuint)(2 * _page));
    }

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The guard-page harness uses mmap/mprotect through libc and runs on Linux only; this platform's over-read protection is unverified.");
        }
    }

    // [Types] every word kind and width the run kernels serve.
    private static IEnumerable<(ABITypeKind Kind, int Width)> RunTypes()
    {
        foreach (int width in new[] { 8, 16, 24, 32, 64, 128, 160, 248, 256 })
        {
            yield return (ABITypeKind.UInt, width);
            yield return (ABITypeKind.Int, width);
            yield return (ABITypeKind.UFixed, width);
            yield return (ABITypeKind.Fixed, width);
        }

        yield return (ABITypeKind.Address, 0);
        yield return (ABITypeKind.Bool, 0);
        yield return (ABITypeKind.Function, 0);
        for (int width = 1; width <= 32; width++)
        {
            yield return (ABITypeKind.FixedBytes, width);
        }
    }

    [TestMethod]
    public void TheRunKernels_NeverReadPastTheEndOfTheirSlice_ForEveryLengthFromZeroTo70AndEveryType()
    {
        RequireLinux();
        using var pages = new GuardedPages();
        int calls = 0;
        foreach (var (kind, width) in RunTypes())
        {
            ReadOnlySpan<byte> mask = ABIWordRun.Mask(kind, width);
            for (int length = 0; length <= 70; length++)
            {
                Span<byte> slice = pages.Span(length);
                slice.Clear();
                // All-zero words are valid for every mask rule; a vector load past the end would fault before the result is compared.
                ABIWordRun.FindFirstRejected(slice, kind, width, out _);
                if (!mask.IsEmpty)
                {
                    ABIWordRun.AllMasked128(slice, mask);
                    ABIWordRun.AllMasked256(slice, mask);
                }

                // The same with a rejected last word (the scalar re-scan must stay inside the slice too).
                if (length >= 32 && length % 32 == 0)
                {
                    slice[^1] = 0xff;
                    slice[^32] = 0xff;
                    ABIWordRun.FindFirstRejected(slice, kind, width, out _);
                    if (!mask.IsEmpty)
                    {
                        ABIWordRun.AllMasked128(slice, mask);
                        ABIWordRun.AllMasked256(slice, mask);
                    }
                }

                calls++;
            }
        }

        Assert.IsGreaterThan(5_000, calls);
    }

    [TestMethod]
    public void TheMaskArgument_IsReadForItsFirst32BytesOnly_EvenWhenItEndsAtTheGuardPage()
    {
        RequireLinux();
        using var pages = new GuardedPages();
        Span<byte> mask = pages.Span(32); // exactly 32 bytes, the last one just before the guard page
        mask.Clear();
        mask[0] = 0xff;
        foreach (int words in new[] { 0, 1, 2, 3, 4, 5, 8, 9 })
        {
            byte[] data = new byte[words * 32];
            Assert.IsTrue(ABIWordRun.AllMasked128(data, mask));
            Assert.IsTrue(ABIWordRun.AllMasked256(data, mask));
        }
    }

    [TestMethod]
    public void ThePaddingWindow_NeverReadsPastTheEndOfTheInput_ForEveryLengthPaddingAndPosition()
    {
        RequireLinux();
        using var pages = new GuardedPages();
        int calls = 0;
        for (int length = 0; length <= 70; length++)
        {
            Span<byte> input = pages.Span(length);
            for (int padding = 0; padding <= 31; padding++)
            {
                for (int paddingStart = 0; paddingStart + padding <= length; paddingStart++)
                {
                    input.Clear();
                    Assert.AreEqual(-1, ABIWordRun.FirstNonZeroPadding(input, paddingStart, padding), $"zero padding: length {length} padding {padding} start {paddingStart}");
                    if (padding > 0)
                    {
                        input[paddingStart + padding - 1] = 1; // the last padding byte, the one a window at the end of the input is most likely to mishandle
                        Assert.AreEqual(padding - 1, ABIWordRun.FirstNonZeroPadding(input, paddingStart, padding), $"last byte: length {length} padding {padding} start {paddingStart}");
                    }

                    calls++;
                }
            }
        }

        Assert.IsGreaterThan(10_000, calls);
    }

    [TestMethod]
    public void WholeDocuments_EndingAtTheGuardPage_AreValidatedWithoutLeavingTheInput_ForEveryArrayLengthAndBytesLength()
    {
        RequireLinux();
        using var pages = new GuardedPages();
        var limits = ABIDecodeLimits.Default;
        int decoded = 0;
        foreach (string element in new[] { "uint8", "address", "bool", "bytes4", "bytes31", "uint256", "int8", "function" })
        {
            ABISchema schema = ABISchemaTestSupport.CompileTuple($"({element}[])");
            for (int count = 0; count <= 70; count++)
            {
                byte[] canonical = [.. Word(0x20), .. Word((ulong)count), .. new byte[count * 32]];
                Span<byte> input = pages.Span(canonical.Length);
                canonical.CopyTo(input);
                Assert.IsTrue(ABIData.TryDecodeComplete(input, schema, in limits, out _, out ABIStatus status), $"{element}[{count}]: {status}");
                decoded++;
            }
        }

        // dynamic bytes of every length, so the padding window sits at the very end of the document
        ABISchema bytesSchema = ABISchemaTestSupport.CompileTuple("(bytes)");
        for (int length = 0; length <= 70; length++)
        {
            byte[] canonical = [.. Word(0x20), .. BytesValue(Enumerable.Repeat((byte)0xab, length).ToArray())];
            Span<byte> input = pages.Span(canonical.Length);
            canonical.CopyTo(input);
            Assert.IsTrue(ABIData.TryDecodeComplete(input, bytesSchema, in limits, out _, out ABIStatus status), $"bytes[{length}]: {status}");
            // and with a dirty last padding byte (the rejection path must stay inside the document too)
            if (length % 32 != 0)
            {
                input[^1] = 1;
                Assert.IsFalse(ABIData.TryDecodeComplete(input, bytesSchema, in limits, out _, out status));
                Assert.AreEqual(ABIStatus.InvalidPadding, status);
            }

            decoded++;
        }

        Assert.IsGreaterThan(500, decoded);
    }
}
