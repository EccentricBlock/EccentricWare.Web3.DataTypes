using System.Buffers.Binary;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Keccak-256 (Ethereum Keccak with the original 0x01 padding, not NIST SHA3-256's 0x06) used for selector derivation
/// and EIP-55 checksums. Allocation-free: the 1600-bit state lives on the stack and the permutation runs on locals.
/// </summary>
public static class Keccak256
{
    private const int RateBytes = 136; // 1088-bit rate for Keccak-256
    private const int OutputBytes = 32;

    private static ReadOnlySpan<ulong> RoundConstants =>
    [
        0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808AUL, 0x8000000080008000UL,
        0x000000000000808BUL, 0x0000000080000001UL, 0x8000000080008081UL, 0x8000000000008009UL,
        0x000000000000008AUL, 0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000AUL,
        0x000000008000808BUL, 0x800000000000008BUL, 0x8000000000008089UL, 0x8000000000008003UL,
        0x8000000000008002UL, 0x8000000000000080UL, 0x000000000000800AUL, 0x800000008000000AUL,
        0x8000000080008081UL, 0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL
    ];

    /// <summary>
    /// Computes Keccak-256(input) into a 32-byte destination buffer.
    /// </summary>
    /// <param name="input">Input bytes.</param>
    /// <param name="hash32">Destination span (must be 32 bytes).</param>
    public static void ComputeHash(ReadOnlySpan<byte> input, Span<byte> hash32)
    {
        if (hash32.Length != OutputBytes)
            ThrowHelper.ThrowArgumentExceptionFixedSize(nameof(hash32), OutputBytes);

        Span<ulong> state = stackalloc ulong[25];
        state.Clear();

        // Absorb full blocks straight from the input.
        while (input.Length >= RateBytes)
        {
            AbsorbBlock(state, input);
            KeccakF1600(state);
            input = input.Slice(RateBytes);
        }

        // Final (possibly empty) block with Keccak padding: 0x01 after the message, 0x80 at the end of the rate.
        Span<byte> block = stackalloc byte[RateBytes];
        block.Clear();
        input.CopyTo(block);
        block[input.Length] ^= 0x01;
        block[RateBytes - 1] ^= 0x80;

        AbsorbBlock(state, block);
        KeccakF1600(state);

        // Squeeze: the 32-byte digest is the first four little-endian lanes.
        for (int i = 0; i < OutputBytes / 8; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(hash32.Slice(i * 8, 8), state[i]);
    }

    /// <summary>
    /// XORs the first 136 bytes of <paramref name="block"/> into the state (little-endian lanes).
    /// </summary>
    private static void AbsorbBlock(Span<ulong> state, ReadOnlySpan<byte> block)
    {
        block = block.Slice(0, RateBytes);
        for (int i = 0; i < RateBytes / 8; i++)
            state[i] ^= BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(i * 8, 8));
    }

    /// <summary>
    /// Keccak-f[1600] permutation (24 rounds). Lanes are indexed x + 5y.
    /// The round body is generated from the specification's rho offsets and pi mapping:
    /// rho(i) = ((t + 1)(t + 2) / 2) mod 64 along the (x, y) -> (y, 2x + 3y) walk, pi(x, y) = (y, 2x + 3y).
    /// </summary>
    private static void KeccakF1600(Span<ulong> state)
    {
        ulong a0 = state[0], a1 = state[1], a2 = state[2], a3 = state[3], a4 = state[4],
              a5 = state[5], a6 = state[6], a7 = state[7], a8 = state[8], a9 = state[9],
              a10 = state[10], a11 = state[11], a12 = state[12], a13 = state[13], a14 = state[14],
              a15 = state[15], a16 = state[16], a17 = state[17], a18 = state[18], a19 = state[19],
              a20 = state[20], a21 = state[21], a22 = state[22], a23 = state[23], a24 = state[24];

        ReadOnlySpan<ulong> roundConstants = RoundConstants;

        for (int round = 0; round < 24; round++)
        {
            // Theta: column parities, then fold D into every lane (fused with Rho + Pi below).
            ulong c0 = a0 ^ a5 ^ a10 ^ a15 ^ a20;
            ulong c1 = a1 ^ a6 ^ a11 ^ a16 ^ a21;
            ulong c2 = a2 ^ a7 ^ a12 ^ a17 ^ a22;
            ulong c3 = a3 ^ a8 ^ a13 ^ a18 ^ a23;
            ulong c4 = a4 ^ a9 ^ a14 ^ a19 ^ a24;
            ulong d0 = c4 ^ BitOperations.RotateLeft(c1, 1);
            ulong d1 = c0 ^ BitOperations.RotateLeft(c2, 1);
            ulong d2 = c1 ^ BitOperations.RotateLeft(c3, 1);
            ulong d3 = c2 ^ BitOperations.RotateLeft(c4, 1);
            ulong d4 = c3 ^ BitOperations.RotateLeft(c0, 1);

            // Rho + Pi: b[pi(i)] = rotl(a[i] ^ d[i % 5], rho(i)).
            ulong b0 = a0 ^ d0;
            ulong b10 = BitOperations.RotateLeft(a1 ^ d1, 1);
            ulong b20 = BitOperations.RotateLeft(a2 ^ d2, 62);
            ulong b5 = BitOperations.RotateLeft(a3 ^ d3, 28);
            ulong b15 = BitOperations.RotateLeft(a4 ^ d4, 27);
            ulong b16 = BitOperations.RotateLeft(a5 ^ d0, 36);
            ulong b1 = BitOperations.RotateLeft(a6 ^ d1, 44);
            ulong b11 = BitOperations.RotateLeft(a7 ^ d2, 6);
            ulong b21 = BitOperations.RotateLeft(a8 ^ d3, 55);
            ulong b6 = BitOperations.RotateLeft(a9 ^ d4, 20);
            ulong b7 = BitOperations.RotateLeft(a10 ^ d0, 3);
            ulong b17 = BitOperations.RotateLeft(a11 ^ d1, 10);
            ulong b2 = BitOperations.RotateLeft(a12 ^ d2, 43);
            ulong b12 = BitOperations.RotateLeft(a13 ^ d3, 25);
            ulong b22 = BitOperations.RotateLeft(a14 ^ d4, 39);
            ulong b23 = BitOperations.RotateLeft(a15 ^ d0, 41);
            ulong b8 = BitOperations.RotateLeft(a16 ^ d1, 45);
            ulong b18 = BitOperations.RotateLeft(a17 ^ d2, 15);
            ulong b3 = BitOperations.RotateLeft(a18 ^ d3, 21);
            ulong b13 = BitOperations.RotateLeft(a19 ^ d4, 8);
            ulong b14 = BitOperations.RotateLeft(a20 ^ d0, 18);
            ulong b24 = BitOperations.RotateLeft(a21 ^ d1, 2);
            ulong b9 = BitOperations.RotateLeft(a22 ^ d2, 61);
            ulong b19 = BitOperations.RotateLeft(a23 ^ d3, 56);
            ulong b4 = BitOperations.RotateLeft(a24 ^ d4, 14);

            // Chi (row-wise) + Iota.
            a0 = b0 ^ (~b1 & b2);
            a1 = b1 ^ (~b2 & b3);
            a2 = b2 ^ (~b3 & b4);
            a3 = b3 ^ (~b4 & b0);
            a4 = b4 ^ (~b0 & b1);
            a5 = b5 ^ (~b6 & b7);
            a6 = b6 ^ (~b7 & b8);
            a7 = b7 ^ (~b8 & b9);
            a8 = b8 ^ (~b9 & b5);
            a9 = b9 ^ (~b5 & b6);
            a10 = b10 ^ (~b11 & b12);
            a11 = b11 ^ (~b12 & b13);
            a12 = b12 ^ (~b13 & b14);
            a13 = b13 ^ (~b14 & b10);
            a14 = b14 ^ (~b10 & b11);
            a15 = b15 ^ (~b16 & b17);
            a16 = b16 ^ (~b17 & b18);
            a17 = b17 ^ (~b18 & b19);
            a18 = b18 ^ (~b19 & b15);
            a19 = b19 ^ (~b15 & b16);
            a20 = b20 ^ (~b21 & b22);
            a21 = b21 ^ (~b22 & b23);
            a22 = b22 ^ (~b23 & b24);
            a23 = b23 ^ (~b24 & b20);
            a24 = b24 ^ (~b20 & b21);
            a0 ^= roundConstants[round];
        }

        state[0] = a0;
        state[1] = a1;
        state[2] = a2;
        state[3] = a3;
        state[4] = a4;
        state[5] = a5;
        state[6] = a6;
        state[7] = a7;
        state[8] = a8;
        state[9] = a9;
        state[10] = a10;
        state[11] = a11;
        state[12] = a12;
        state[13] = a13;
        state[14] = a14;
        state[15] = a15;
        state[16] = a16;
        state[17] = a17;
        state[18] = a18;
        state[19] = a19;
        state[20] = a20;
        state[21] = a21;
        state[22] = a22;
        state[23] = a23;
        state[24] = a24;
    }
}
