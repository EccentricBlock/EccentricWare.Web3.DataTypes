using System.Reflection;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EccentricWare.Web3.DataTypes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public class Keccak256Tests
{
    // All digests below come from an independent Keccak-256 implementation (original 0x01 padding, not SHA-3) written from the
    // specification (round constants from the LFSR, rho/pi from the walk), validated against the published vectors for
    // "", "abc", the quick brown fox and transfer(address,uint256) => 0xa9059cbb.

    private static string Hash(ReadOnlySpan<byte> input)
    {
        Span<byte> hash = stackalloc byte[32];
        Keccak256.ComputeHash(input, hash);
        return Convert.ToHexStringLower(hash);
    }

    private static byte[] Pattern(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i % 251);
        return data;
    }

    [TestMethod]
    [DataRow("", "c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470")]
    [DataRow("abc", "4e03657aea45a94fc7d47ba826c8d667c0d1e6e33a64a036ec44f58fa12d6c45")]
    [DataRow("The quick brown fox jumps over the lazy dog", "4d741b6f1eb29cb2a9b9911c82f56fa8d73b04959d3d9d222895df6c0b28aa15")]
    [DataRow("hello world", "47173285a8d7341e5e972fc677286384f802f8ef42a5ec5f03bbfa254cb01fad")]
    public void ComputeHash_TextVectors_MatchPublishedDigests(string text, string expectedHex)
        => Assert.AreEqual(expectedHex, Hash(Encoding.UTF8.GetBytes(text)));

    [TestMethod]
    public void ComputeHash_TransferSignature_GivesErc20TransferSelector()
        => Assert.AreEqual("a9059cbb", Hash("transfer(address,uint256)"u8)[..8]);

    [TestMethod]
    public void ComputeHash_EveryEmptyInputForm_GivesEmptyDigest()
    {
        const string expected = "c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470";
        Assert.AreEqual(expected, Hash(ReadOnlySpan<byte>.Empty));
        Assert.AreEqual(expected, Hash(default));
        Assert.AreEqual(expected, Hash(Array.Empty<byte>()));
    }

    [TestMethod]
    public void ComputeHash_IsNotNistSha3_256()
    {
        // SHA3-256 uses the same permutation with padding byte 0x06 instead of 0x01, so the digests differ.
        Assert.AreNotEqual("a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a", Hash(ReadOnlySpan<byte>.Empty)); // SHA3-256("")
        Assert.AreNotEqual("3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532", Hash("abc"u8)); // SHA3-256("abc")
    }

    // Zero bytes: only the padding distinguishes lengths. 135 is the single-pad-byte case (0x01 and 0x80 merge to 0x81);
    // 136 and 272 add a whole padding block.
    [TestMethod]
    [DataRow(134, "20926439d7d1663c4d224931643bb513f3ebf181ab353278b24399f518dc5f97")]
    [DataRow(135, "29e3704feeca7fb9ba229f0fa04d9b36449cf3ad6e1d85d9cfff3a10df9abc3e")]
    [DataRow(136, "3a5912a7c5faa06ee4fe906253e339467a9ce87d533c65be3c15cb231cdb25f9")]
    [DataRow(137, "bee7fbb405cb0d91a8775e338c4a5e4b5d6b2d051f687fa942043cffdc73bd28")]
    [DataRow(271, "3bb611e98ca876adc01436a582979ecfd012389033aca7dbf76dcb424fe02a0c")]
    [DataRow(272, "a8005c7a3125b6c3629b4181eca54d18721e41fef639718d205beb00b366ed7d")]
    [DataRow(273, "20494ff4ca5f71c4edb30f159611d235a403fdd6beb95a7c386176842c3fd109")]
    [DataRow(407, "a8a6ebaa9d110b6c3e2be99af22bced6155619534956bc9a03eb8b9331b3ce71")]
    [DataRow(408, "a70b3027c607507cca393052d59fe207dca1e1e8e79971265681bb86a021ddb3")]
    [DataRow(409, "b2c40d33b4de6aef018f023b93ca3c8e65c41630650f72c445c5f41720593136")]
    public void ComputeHash_ZeroBytes_PinsPaddingAcrossBlockBoundaries(int length, string expectedHex)
        => Assert.AreEqual(expectedHex, Hash(new byte[length]));

    // Pattern i % 251.
    [TestMethod]
    [DataRow(1, "bc36789e7a1e281436464229828f817d6612f7b477d66591ff96a9e064bcc98a")]
    [DataRow(2, "49d03a195e239b52779866b33024210fc7dc66e9c2998975c0aa45c1702549d5")]
    [DataRow(55, "797f92d5e159d80a886c0a7802a255b475a1e8e473e8cf4345144824a2aee79c")]
    [DataRow(56, "0d0bf4902a749dee22eae5f1b6e2b867ba696ce9be7632eba14315ac09bd1856")]
    [DataRow(64, "002030bde3d4cf89919649775cd71875c4d0ab1708a380e03fefc3a28aa24831")]
    [DataRow(100, "816afb32e7661842252bc955167ea1e36fde4ac8cfe932978b9dcdb04aee8ca4")]
    [DataRow(134, "861e165162f806cd361c4421a48f205820ddf4deb02db9f041f48e179ddada97")]
    [DataRow(135, "cbdfd9dee5faad3818d6b06f95a219fd290b0e1706f6a82e5a595b9ce9faca62")]
    [DataRow(136, "7ce759f1ab7f9ce437719970c26b0a66ff11fe3e38e17df89cf5d29c7d7f807e")]
    [DataRow(137, "ac73d4fae68b8453f764007c1a20ce95994187861f0c3227a3a8e99a73a3b1db")]
    [DataRow(138, "9dff24f078dfc5f2858894ee1f79728c3e4be850a0fccc5929bba850ca98efa1")]
    [DataRow(200, "bfb0aa97863e797943cf7c33bb7e880bb4543f3d2703c0923c6901c2af57b890")]
    [DataRow(271, "27eceb59ebc3dc8a04a5b135be641591a7278540e4556a2ba9f408194e666ec3")]
    [DataRow(272, "8e2476e65823b24d96ebe239f2c1534cdf763e689e2410c3b1cb0c74e6177bfc")]
    [DataRow(273, "3f02f134370e4debb95140ef49ddd3aed8c65ff1ed83a43f1b269421f179c5f9")]
    [DataRow(408, "2fa03dab557315e48395073815c6aefeb1e78b5f750fa85b623ab3a1f1d988a1")]
    [DataRow(409, "0ff7e0419de009e67c976466d11b9639e5c174c4c46bab0586c04c9440edf088")]
    [DataRow(1000, "af692982e84a5a9688359025660a7857cd28ee7c8d867cfa1677baf2e6d1f63b")]
    public void ComputeHash_PatternInputs_MatchReferenceAcrossBlockBoundaries(int length, string expectedHex)
        => Assert.AreEqual(expectedHex, Hash(Pattern(length)));

    [TestMethod]
    [DataRow(135, "34367dc248bbd832f4e3e69dfaac2f92638bd0bbd18f2912ba4ef454919cf446")]
    [DataRow(136, "a6c4d403279fe3e0af03729caada8374b5ca54d8065329a3ebcaeb4b60aa386e")]
    [DataRow(137, "d869f639c7046b4929fc92a4d988a8b22c55fbadb802c0c66ebcd484f1915f39")]
    [DataRow(271, "132f47effd6c8b1b299efa53fe68aece77ec8ae4eb2e294f668eec94f76001e1")]
    [DataRow(272, "cf7fcd4f705ee749930d19ca84561a9bf62516bd90a471545fa2f49fdc7e63c8")]
    [DataRow(273, "5a7b8187d2778e614097fac3097573de1fee4d972304d3360796a857029bb176")]
    [DataRow(1_000_000, "fadae6b49f129bbb812be8407b7b2894f34aecf6dbd1f9b0f0c7e9853098fc96")]
    public void ComputeHash_RepeatedLetterA_MatchesReference(int length, string expectedHex)
        => Assert.AreEqual(expectedHex, Hash(Encoding.ASCII.GetBytes(new string('a', length))));

    [TestMethod]
    public void ComputeHash_EveryLengthUpTo1200_MatchesReferenceDigestStream()
    {
        // SHA-256 over the concatenated Keccak digests of pattern inputs of length 0..1200 (every residue mod 136 and
        // many absorbed-block counts); the stream digest comes from the independent reference implementation.
        byte[] stream = new byte[1201 * 32];
        byte[] input = Pattern(1200);
        for (int length = 0; length <= 1200; length++)
            Keccak256.ComputeHash(input.AsSpan(0, length), stream.AsSpan(length * 32, 32));

        Assert.AreEqual("2b5e948222e5259c550b5aaf854f99b62df38b0c08552835172d7bd117ba72d3", Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(31)]
    [DataRow(33)]
    [DataRow(64)]
    public void ComputeHash_WrongDestinationLength_ThrowsAndLeavesDestinationUntouched(int length)
    {
        byte[] destination = new byte[length];
        Array.Fill(destination, (byte)0xEE);

        var ex = Assert.ThrowsExactly<ArgumentException>(() => Keccak256.ComputeHash("abc"u8, destination));

        Assert.AreEqual("hash32", ex.ParamName);
        StringAssert.Contains(ex.Message, "Destination must be exactly 32 elements long.");
        Assert.IsTrue(destination.All(b => b == 0xEE));
    }

    [TestMethod]
    public void ComputeHash_InputOverlappingOutputAtStart_StillProducesCorrectDigest()
    {
        byte[] message = Pattern(200);
        string expected = Hash(message);

        byte[] shared = (byte[])message.Clone();
        Keccak256.ComputeHash(shared.AsSpan(0, 200), shared.AsSpan(0, 32));

        Assert.AreEqual(expected, Convert.ToHexStringLower(shared.AsSpan(0, 32)));
    }

    [TestMethod]
    [DataRow(100, 200)] // output inside the input
    [DataRow(168, 200)] // output overlaps the tail of the input
    [DataRow(190, 200)] // output starts inside the input and ends past it
    [DataRow(0, 408)]   // multi-block input, output at the start
    [DataRow(380, 408)] // multi-block input, output inside the last block
    public void ComputeHash_InputPartiallyOverlappingOutput_StillProducesCorrectDigest(int outputOffset, int inputLength)
    {
        byte[] message = Pattern(inputLength);
        string expected = Hash(message);

        byte[] shared = new byte[Math.Max(inputLength, outputOffset + 32)];
        message.CopyTo(shared, 0);
        Keccak256.ComputeHash(shared.AsSpan(0, inputLength), shared.AsSpan(outputOffset, 32));

        Assert.AreEqual(expected, Convert.ToHexStringLower(shared.AsSpan(outputOffset, 32)));
    }

    [TestMethod]
    public void ComputeHash_DoesNotModifyInput_AndIsDeterministic()
    {
        byte[] input = Pattern(300);
        byte[] copy = (byte[])input.Clone();
        byte[] first = new byte[32];
        byte[] second = new byte[32];

        Keccak256.ComputeHash(input, first);
        Keccak256.ComputeHash(input, second);

        CollectionAssert.AreEqual(copy, input);
        CollectionAssert.AreEqual(first, second);
    }

    [TestMethod]
    public void ComputeHash_OverwritesPreviousDestinationContents()
    {
        byte[] destination = new byte[32];
        Array.Fill(destination, (byte)0xFF);

        Keccak256.ComputeHash("abc"u8, destination);

        Assert.AreEqual("4e03657aea45a94fc7d47ba826c8d667c0d1e6e33a64a036ec44f58fa12d6c45", Convert.ToHexStringLower(destination));
    }

    [TestMethod]
    public void ComputeHash_SingleBitFlipAtAnyPosition_ChangesDigest()
    {
        byte[] input = Pattern(150); // crosses the 136-byte rate
        string baseline = Hash(input);
        var seen = new HashSet<string> { baseline };

        for (int i = 0; i < input.Length; i++)
        {
            input[i] ^= 0x01;
            Assert.IsTrue(seen.Add(Hash(input)), $"flipping bit 0 of byte {i} collided");
            input[i] ^= 0x01;
        }

        Assert.AreEqual(baseline, Hash(input));
    }

    [TestMethod]
    public void ComputeHash_ThousandSeededRandomInputs_NeverShareADigest()
    {
        var random = new Random(20240601);
        var inputByDigest = new Dictionary<string, string>();
        for (int i = 0; i < 1000; i++)
        {
            byte[] input = new byte[random.Next(0, 400)];
            random.NextBytes(input);
            string inputHex = Convert.ToHexString(input);
            string digest = Hash(input);

            if (inputByDigest.TryGetValue(digest, out string? other))
                Assert.AreEqual(other, inputHex, "two different inputs produced the same digest");
            inputByDigest[digest] = inputHex;
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(135)]
    [DataRow(136)]
    [DataRow(10_000)]
    public void ComputeHash_DoesNotAllocateOnTheHeap(int length)
    {
        // The guarantee belongs to optimised code. Unoptimised (Debug) JIT code allocates 72 bytes per absorbed block (measured:
        // 144 bytes for 136 input bytes, 5,328 for 10,000), a Debug artefact that would make any fixed bound brittle.
        bool minOpts = Environment.GetEnvironmentVariable("DOTNET_JITMinOpts") == "1" || Environment.GetEnvironmentVariable("COMPlus_JITMinOpts") == "1";
        bool optimised = !minOpts && typeof(Keccak256).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };
        if (!optimised)
            Assert.Inconclusive("Zero allocation is asserted for optimised builds only.");

        byte[] input = Pattern(length);
        byte[] destination = new byte[32];
        for (int i = 0; i < 5; i++)
            Keccak256.ComputeHash(input, destination); // warm-up

        long minimum = long.MaxValue;
        for (int trial = 0; trial < 20; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Keccak256.ComputeHash(input, destination);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.AreEqual(0, minimum);
    }
}
