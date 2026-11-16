namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Equality, hashing and ordering contracts of <see cref="Signature"/>, isolated per field and per type.</summary>
[TestClass]
public sealed class SignatureEqualityOrderingTests
{
    /// <summary>Order from the documentation (type, payload big-endian, v), built only from <c>ToBytes</c> and <c>V</c>.</summary>
    private sealed class IndependentOrder : IComparer<Signature>
    {
        public int Compare(Signature x, Signature y)
        {
            int type = ((byte)x.Type).CompareTo((byte)y.Type);
            if (type != 0)
                return type;

            int payload = x.ToBytes().AsSpan(0, 64).SequenceCompareTo(y.ToBytes().AsSpan(0, 64));
            return payload != 0 ? Math.Sign(payload) : Math.Sign(x.V - y.V);
        }
    }

    private static byte[] OnlyByteSet(int index)
    {
        byte[] bytes = new byte[64];
        bytes[index] = 1;
        return bytes;
    }

    /// <summary>Strictly increasing by construction: lower byte index means more significant, v is least significant.</summary>
    private static Signature[] StrictlyIncreasingChain()
    {
        byte[] zeros = new byte[64];
        byte[] ones = SignatureTestData.Filled(64, 0xFF);
        return
        [
            SignatureTestData.EvmFrom(zeros, 0),
            SignatureTestData.EvmFrom(zeros, 1),
            SignatureTestData.EvmFrom(zeros, 0x7F),
            SignatureTestData.EvmFrom(zeros, 0x80),
            SignatureTestData.EvmFrom(zeros, 0xFF),
            SignatureTestData.EvmFrom(OnlyByteSet(63), 0),
            SignatureTestData.EvmFrom(OnlyByteSet(63), 0xFF),
            SignatureTestData.EvmFrom(OnlyByteSet(62), 0),
            SignatureTestData.EvmFrom(OnlyByteSet(56), 0),
            SignatureTestData.EvmFrom(OnlyByteSet(32), 0),
            SignatureTestData.EvmFrom(OnlyByteSet(31), 0),
            SignatureTestData.EvmFrom(OnlyByteSet(8), 0),
            SignatureTestData.EvmFrom(OnlyByteSet(0), 0),
            SignatureTestData.EvmFrom(ones, 0),
            SignatureTestData.EvmFrom(ones, 0xFF),
            Signature.FromSolanaBytes(zeros),
            Signature.FromSolanaBytes(OnlyByteSet(63)),
            Signature.FromSolanaBytes(OnlyByteSet(0)),
            Signature.FromSolanaBytes(ones),
        ];
    }

    // ----------------- equality -----------------

    [TestMethod]
    public void Equals_RequiresSameTypePayloadAndV()
    {
        byte[] bytes = SignatureTestData.Ascending65();
        Signature evm = Signature.FromEvmBytes(bytes);

        Assert.AreEqual(evm, Signature.FromEvmBytes(bytes));
        Assert.IsTrue(evm == Signature.FromEvmBytes(bytes));
        Assert.IsFalse(evm != Signature.FromEvmBytes(bytes));
        Assert.IsTrue(evm.Equals((object)Signature.FromEvmBytes(bytes)));
        Assert.IsFalse(evm.Equals((object?)null));
        Assert.IsFalse(evm.Equals("not a signature"));
        Assert.IsFalse(evm.Equals((object)SignatureTestData.Ascending65Hex));

        // v differs in each bit, nothing else does.
        for (int bit = 0; bit < 8; bit++)
        {
            byte[] otherV = (byte[])bytes.Clone();
            otherV[64] ^= (byte)(1 << bit);
            Signature other = Signature.FromEvmBytes(otherV);
            Assert.AreNotEqual(evm, other, $"v bit {bit}");
            Assert.IsTrue(evm != other);
            Assert.IsFalse(evm == other);
        }
    }

    [TestMethod]
    public void Equals_DetectsASingleBitChangeInEveryPayloadByte()
    {
        byte[] bytes = SignatureTestData.Ascending65();
        Signature evm = Signature.FromEvmBytes(bytes);
        Signature solana = Signature.FromSolanaBytes(bytes.AsSpan(0, 64));

        for (int index = 0; index < 64; index++)
        {
            byte[] changed = (byte[])bytes.Clone();
            changed[index] ^= 0x80;

            Assert.AreNotEqual(evm, Signature.FromEvmBytes(changed), $"EVM byte {index}");
            Assert.AreNotEqual(solana, Signature.FromSolanaBytes(changed.AsSpan(0, 64)), $"Solana byte {index}");
        }
    }

    [TestMethod]
    public void SameBytesOfDifferentType_AreNeverEqual_HashEqual_OrOrderedTogether()
    {
        foreach (byte[] payload in SignatureTestData.Payloads64())
        {
            // v == 0 on the EVM side isolates the type as the only difference.
            Signature evm = SignatureTestData.EvmFrom(payload, 0);
            Signature solana = Signature.FromSolanaBytes(payload);
            string label = Convert.ToHexString(payload, 0, 4);

            Assert.IsFalse(evm.Equals(solana), label);
            Assert.IsFalse(solana.Equals(evm), label);
            Assert.IsFalse(evm == solana, label);
            Assert.IsTrue(evm != solana, label);
            Assert.IsFalse(evm.Equals((object)solana), label);

            // Equal r and s hash identically, so the two hashes differ exactly by the type bit: deterministic.
            Assert.AreNotEqual(evm.GetHashCode(), solana.GetHashCode(), label);

            // The type decides before the payload, even when the EVM payload is the numerically largest.
            Assert.IsLessThan(0, evm.CompareTo(solana), label);
            Assert.IsGreaterThan(0, solana.CompareTo(evm), label);
            Assert.IsTrue(evm < solana && evm <= solana && !(evm >= solana) && !(evm > solana), label);
            Assert.IsTrue(solana > evm && solana >= evm && !(solana <= evm) && !(solana < evm), label);

            var set = new HashSet<Signature> { evm };
            Assert.IsFalse(set.Contains(solana), label);
            set.Add(solana);
            Assert.HasCount(2, set);
            var dictionary = new Dictionary<Signature, string> { [evm] = "evm", [solana] = "solana" };
            Assert.HasCount(2, dictionary);
            Assert.AreEqual("solana", dictionary[solana]);
        }

        Assert.IsLessThan(0, SignatureTestData.EvmFrom(SignatureTestData.Filled(64, 0xFF), 0xFF).CompareTo(Signature.ZeroSolana));
    }

    [TestMethod]
    public void DefaultAndZeros_RelateByType()
    {
        Assert.IsTrue(default(Signature) == Signature.ZeroEvm);
        Assert.IsTrue(default(Signature) != Signature.ZeroSolana);
        Assert.AreEqual(Signature.ZeroEvm.GetHashCode(), default(Signature).GetHashCode());
        Assert.AreNotEqual(Signature.ZeroSolana.GetHashCode(), default(Signature).GetHashCode());
        Assert.AreEqual(0, default(Signature).CompareTo(Signature.ZeroEvm));
        Assert.IsLessThan(0, default(Signature).CompareTo(Signature.ZeroSolana));
    }

    // ----------------- hashing -----------------

    [TestMethod]
    public void GetHashCode_EqualValues_HaveEqualHashes()
    {
        foreach (Signature signature in SignatureTestData.RandomSignatures(200))
        {
            Signature copy = signature.Type == SignatureType.Evm
                ? Signature.FromEvmBytes(signature.ToBytes())
                : Signature.FromSolanaBytes(signature.ToBytes());

            Assert.AreEqual(signature, copy);
            Assert.AreEqual(signature.GetHashCode(), copy.GetHashCode());
        }
    }

    [TestMethod]
    public void GetHashCode_UsesEveryByte_SoSignaturesSharingHalfAreNotFlooded()
    {
        // A malicious producer can choose signature bytes. If the hash ignored r (or s), 10,000 signatures sharing one
        // half would all land in one dictionary bucket. The hash is keyed per process, so use a near-complete
        // distinctness bound rather than exact values.
        const int Count = 10_000;
        var random = new Random(7);
        byte[] fixedHalf = SignatureTestData.RandomBytes(random, 32);

        var sharedR = new HashSet<int>();
        var sharedS = new HashSet<int>();
        var onlyLastByte = new HashSet<int>();
        for (int i = 0; i < Count; i++)
        {
            byte[] varying = SignatureTestData.RandomBytes(random, 32);

            sharedR.Add(Signature.FromEvmComponents(fixedHalf, varying, 27).GetHashCode());
            sharedS.Add(Signature.FromEvmComponents(varying, fixedHalf, 27).GetHashCode());

            // Only the final 16 bits of the payload vary (counter in the last two bytes).
            byte[] counter = new byte[64];
            counter[62] = (byte)(i >> 8);
            counter[63] = (byte)i;
            onlyLastByte.Add(Signature.FromSolanaBytes(counter).GetHashCode());
        }

        Assert.IsGreaterThan(Count * 99 / 100, sharedR.Count, "r shared");
        Assert.IsGreaterThan(Count * 99 / 100, sharedS.Count, "s shared");
        Assert.IsGreaterThan(Count * 99 / 100, onlyLastByte.Count, "counter in last bytes");
    }

    [TestMethod]
    public void GetHashCode_TypeAndVParticipate()
    {
        byte[] payload = SignatureTestData.Ascending64();

        var hashes = new HashSet<int>();
        for (int v = 0; v < 256; v++)
            hashes.Add(SignatureTestData.EvmFrom(payload, (byte)v).GetHashCode());
        hashes.Add(Signature.FromSolanaBytes(payload).GetHashCode());

        Assert.HasCount(257, hashes, "r and s are fixed, so v (256 values) and the type give 257 distinct hashes");
    }

    [TestMethod]
    public void GetHashCode_ChangesWhenAnySinglePayloadOrVBitFlips()
    {
        // The hash is keyed per process, so only distinctness is asserted: 512 payload flips may collide with a
        // probability of about 2^-23 in total; allow 1% anyway. Flipping a v bit changes the meta term, so it always differs.
        byte[] payload = SignatureTestData.RandomBytes(new Random(512), 64);
        foreach (bool solana in new[] { false, true })
        {
            Signature original = solana ? Signature.FromSolanaBytes(payload) : SignatureTestData.EvmFrom(payload, 0x1b);
            int differing = 0;
            for (int bit = 0; bit < 512; bit++)
            {
                byte[] flipped = (byte[])payload.Clone();
                flipped[bit / 8] ^= (byte)(1 << (bit % 8));
                Signature changed = solana ? Signature.FromSolanaBytes(flipped) : SignatureTestData.EvmFrom(flipped, 0x1b);
                if (changed.GetHashCode() != original.GetHashCode())
                    differing++;
            }

            Assert.IsGreaterThanOrEqualTo(512 * 99 / 100, differing, solana ? "Solana" : "EVM");
        }

        Signature evm = SignatureTestData.EvmFrom(payload, 0x1b);
        for (int bit = 0; bit < 8; bit++)
            Assert.AreNotEqual(evm.GetHashCode(), SignatureTestData.EvmFrom(payload, (byte)(0x1b ^ (1 << bit))).GetHashCode(), $"v bit {bit}");
    }

    [TestMethod]
    public void GetHashCode_DistinguishesSwappedHalves()
    {
        var random = new Random(31337);
        for (int i = 0; i < 10; i++)
        {
            byte[] r = SignatureTestData.RandomBytes(random, 32);
            byte[] s = SignatureTestData.RandomBytes(random, 32);
            Signature rs = Signature.FromEvmComponents(r, s, 27);
            Signature sr = Signature.FromEvmComponents(s, r, 27);

            Assert.AreNotEqual(rs, sr);
            Assert.AreNotEqual(rs.GetHashCode(), sr.GetHashCode(), $"pair {i}");
        }
    }

    [TestMethod]
    public void GetHashCode_IsStable_AcrossRepeatedAndParallelCalls()
    {
        Signature[] items = SignatureTestData.RandomSignatures(64).ToArray();
        int[] expected = items.Select(s => s.GetHashCode()).ToArray();

        int mismatches = 0;
        Parallel.For(0, 2000, i =>
        {
            int index = i % items.Length;
            if (items[index].GetHashCode() != expected[index])
                Interlocked.Increment(ref mismatches);
        });

        Assert.AreEqual(0, mismatches);
    }

    [TestMethod]
    public void Signature_WorksAsDictionaryAndHashSetKey_ForBothTypes()
    {
        Signature[] items = SignatureTestData.RandomSignatures(500).ToArray();
        var dictionary = new Dictionary<Signature, int>();
        var set = new HashSet<Signature>(items);
        for (int i = 0; i < items.Length; i++)
            dictionary[items[i]] = i;

        Assert.HasCount(items.Length, dictionary);
        Assert.HasCount(items.Length, set);
        for (int i = 0; i < items.Length; i++)
        {
            Assert.AreEqual(i, dictionary[items[i]]);
            Assert.IsTrue(set.Contains(items[i]));
        }

        // Rebuilt copies are found, and the same payload as the other type is a different key.
        foreach (Signature item in items.Take(50))
        {
            Signature sameType = item.Type == SignatureType.Evm ? Signature.FromEvmBytes(item.ToBytes()) : Signature.FromSolanaBytes(item.ToBytes());
            Signature otherType = item.Type == SignatureType.Evm ? Signature.FromSolanaBytes(item.ToBytes().AsSpan(0, 64)) : SignatureTestData.EvmFrom(item.ToBytes(), 0);
            Assert.IsTrue(set.Contains(sameType));
            Assert.IsFalse(set.Contains(otherType));
        }
    }

    // ----------------- ordering -----------------

    [TestMethod]
    public void CompareTo_OrdersByType_ThenPayloadBigEndian_ThenV()
    {
        Signature lowEvm = Signature.FromEvmBytes(SignatureTestData.Ascending65());
        byte[] bigger = SignatureTestData.Ascending65();
        bigger[0] = 0xFF;
        Signature highEvm = Signature.FromEvmBytes(bigger);
        byte[] higherV = SignatureTestData.Ascending65();
        higherV[64] = 0xFF;
        Signature highV = Signature.FromEvmBytes(higherV);
        Signature solanaLow = Signature.FromSolanaBytes(new byte[64]);

        Assert.IsLessThan(0, lowEvm.CompareTo(highEvm));
        Assert.IsGreaterThan(0, highEvm.CompareTo(lowEvm));
        Assert.AreEqual(0, lowEvm.CompareTo(Signature.FromEvmBytes(SignatureTestData.Ascending65())));
        Assert.IsLessThan(0, lowEvm.CompareTo(highV)); // payload equal, v decides
        Assert.IsGreaterThan(0, highV.CompareTo(lowEvm)); // v greater direction
        Assert.IsLessThan(0, highEvm.CompareTo(solanaLow)); // type decides before payload
        Assert.IsGreaterThan(0, solanaLow.CompareTo(highEvm));
    }

    [TestMethod]
    public void CompareTo_TreatsVAsUnsignedAndLeastSignificant()
    {
        byte[] vs = [0, 1, 27, 28, 0x7F, 0x80, 0xFE, 0xFF];
        byte[] payload = SignatureTestData.Ascending64();
        for (int i = 0; i < vs.Length; i++)
        {
            for (int j = 0; j < vs.Length; j++)
            {
                Signature a = SignatureTestData.EvmFrom(payload, vs[i]);
                Signature b = SignatureTestData.EvmFrom(payload, vs[j]);
                Assert.AreEqual(Math.Sign(i - j), Math.Sign(a.CompareTo(b)), $"v {vs[i]:x2} vs {vs[j]:x2}");
            }
        }

        // One payload step outweighs the whole v range.
        byte[] higherPayload = (byte[])payload.Clone();
        higherPayload[63]++;
        Assert.IsLessThan(0, SignatureTestData.EvmFrom(payload, 0xFF).CompareTo(SignatureTestData.EvmFrom(higherPayload, 0)));
    }

    [TestMethod]
    public void CompareTo_AndOperators_FollowTheStrictlyIncreasingChain_ForEveryPair()
    {
        Signature[] chain = StrictlyIncreasingChain();
        for (int i = 0; i < chain.Length; i++)
        {
            for (int j = 0; j < chain.Length; j++)
            {
                Signature a = chain[i];
                Signature b = chain[j];
                int sign = Math.Sign(i - j);
                string label = $"{i} vs {j}";

                Assert.AreEqual(sign, Math.Sign(a.CompareTo(b)), label);
                Assert.AreEqual(sign, Math.Sign(((IComparable<Signature>)a).CompareTo(b)), label);
                Assert.AreEqual(sign, Math.Sign(((IComparable)a).CompareTo((object)b)), label);
                Assert.AreEqual(sign, Math.Sign(Comparer<Signature>.Default.Compare(a, b)), label);
                Assert.AreEqual(sign < 0, a < b, label);
                Assert.AreEqual(sign > 0, a > b, label);
                Assert.AreEqual(sign <= 0, a <= b, label);
                Assert.AreEqual(sign >= 0, a >= b, label);
                Assert.AreEqual(sign == 0, a == b, label);
                Assert.AreEqual(sign != 0, a != b, label);
                Assert.AreEqual(sign == 0, a.Equals(b), label);
            }
        }
    }

    [TestMethod]
    public void CompareTo_IsAntisymmetric_AndAgreesWithTheIndependentOrder_ForRandomAndEdgePairs()
    {
        var order = new IndependentOrder();
        Signature[] items = SignatureTestData.RandomSignatures(60).Concat(StrictlyIncreasingChain()).ToArray();

        foreach (Signature a in items)
        {
            foreach (Signature b in items)
            {
                int forward = Math.Sign(a.CompareTo(b));
                Assert.AreEqual(-forward, Math.Sign(b.CompareTo(a)));
                Assert.AreEqual(Math.Sign(order.Compare(a, b)), forward);
                Assert.AreEqual(forward == 0, a.Equals(b));
            }
        }
    }

    [TestMethod]
    public void CompareTo_AgreesWithByteWiseOrderOfTheEncodedForm_WithinOneType()
    {
        // RandomSignatures alternates EVM / Solana, so group by type first: adjacent items of one type are never skipped.
        int compared = 0;
        foreach (IGrouping<SignatureType, Signature> group in SignatureTestData.RandomSignatures(600).GroupBy(s => s.Type))
        {
            Signature[] items = group.ToArray();
            for (int i = 0; i + 1 < items.Length; i++)
            {
                int expected = Math.Sign(items[i].ToBytes().AsSpan().SequenceCompareTo(items[i + 1].ToBytes()));
                Assert.AreEqual(expected, Math.Sign(items[i].CompareTo(items[i + 1])), $"{group.Key} pair {i}");
                compared++;
            }
        }

        Assert.IsGreaterThan(500, compared, "the comparison loop must actually run");
    }

    [TestMethod]
    public void CompareTo_FlipsOnTheLastPayloadByte_ForEveryLimbBoundary()
    {
        foreach (int index in new[] { 0, 7, 8, 15, 16, 23, 24, 31, 32, 39, 40, 47, 48, 55, 56, 63 })
        {
            byte[] low = new byte[65];
            byte[] high = new byte[65];
            high[index] = 1;
            Assert.IsLessThan(0, Signature.FromEvmBytes(low).CompareTo(Signature.FromEvmBytes(high)), $"index {index}");
            Assert.IsGreaterThan(0, Signature.FromEvmBytes(high).CompareTo(Signature.FromEvmBytes(low)), $"index {index}");
        }
    }

    [TestMethod]
    public void CompareTo_Object_HandlesNullAndWrongType()
    {
        Signature value = SignatureTestData.SampleEvm;
        Assert.AreEqual(1, value.CompareTo((object?)null));
        Assert.AreEqual(1, ((IComparable)value).CompareTo(null));
        Assert.AreEqual(0, value.CompareTo((object)SignatureTestData.SampleEvm));

        var error = Assert.ThrowsExactly<ArgumentException>(() => value.CompareTo("text"));
        Assert.AreEqual("obj", error.ParamName);
        StringAssert.Contains(error.Message, nameof(Signature));
        Assert.ThrowsExactly<ArgumentException>(() => value.CompareTo((object)SignatureTestData.Ascending65()));
    }

    [TestMethod]
    public void Array_Sort_MatchesTheIndependentOrder()
    {
        Signature[] items = SignatureTestData.RandomSignatures(400).Concat(StrictlyIncreasingChain()).ToArray();
        Signature[] sorted = (Signature[])items.Clone();
        Array.Sort(sorted);

        Signature[] expected = items.Order(new IndependentOrder()).ToArray();
        CollectionAssert.AreEqual(expected, sorted);

        // All EVM before all Solana.
        int firstSolana = Array.FindIndex(sorted, s => s.Type == SignatureType.Solana);
        Assert.IsGreaterThan(0, firstSolana);
        Assert.IsTrue(sorted.Skip(firstSolana).All(s => s.Type == SignatureType.Solana));
    }
}
