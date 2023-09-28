using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AtelierTool;

public static class BundleCrypto
{
    public static Stream DecryptBundle(byte[] bundleData, Bundle bundle)
    {
        var bundleSpan = bundleData.AsSpan();

        if (!CheckHeader(bundleSpan, out var header))
            throw new InvalidOperationException("Bundle had an invalid header.");

        var encBundleHash = bundleSpan.Slice(Header.Size, Header.HashSize);
        var encBundle = bundleSpan[(Header.Size + Header.HashSize)..];

        var bundleDataHash = MD5.HashData(encBundle).AsSpan();
        if (!bundleDataHash.SequenceEqual(encBundleHash))
            throw new InvalidOperationException("Encrypted bundle hash mismatch.");

        if (header.Encrypted == 1)
        {
            var keyMaterial = $"{bundle.BundleName}-{bundle.FileSize - Header.Size - Header.HashSize}-{bundle.Hash}-{bundle.Crc}";

            var hash = SHA512.HashData(Encoding.UTF8.GetBytes(keyMaterial));
            var key = hash[..0x20];
            var nonceMaterial = SHA512.HashData(hash);

            var counter = 0;

            var context = (stackalloc byte[0x40 * 8]); // 8 ChaCha contexts
            context.Clear();

            var nonce = (stackalloc byte[12]);
            nonce.Clear();

            var blockCount = encBundle.Length / context.Length;
            var lastBlockCount = encBundle.Length % context.Length;

            if (blockCount > 0)
            {
                for (int i = 0; i < blockCount; i++)
                {
                    GenerateKeyStream(ref context, ref nonce, ref counter, key, nonceMaterial);

                    var partData = MemoryMarshal.Cast<byte, ulong>(encBundle.Slice(i * context.Length, context.Length));
                    var keyStream = MemoryMarshal.Cast<byte, ulong>(context);
                    for (int j = 0; j < context.Length / 8; j++)
                    {
                        partData[j] ^= keyStream[j];
                    }
                }
            }

            if (lastBlockCount > 0)
            {
                GenerateKeyStream(ref context, ref nonce, ref counter, key, nonceMaterial);

                for (int i = 0; i < lastBlockCount; i++)
                {
                    encBundle[i + context.Length * blockCount] ^= context[i];
                }
            }
        }

        var ms = new MemoryStream(encBundle.ToArray());
        return ms;
    }

    private static bool CheckHeader(Span<byte> bundle, out Header header)
    {
        header = default;

        if (Header.Size + Header.HashSize > bundle.Length)
            return false;

        header = MemoryMarshal.Cast<byte, Header>(bundle[..Header.Size])[0];

        if (header.Magic != 0x6b746b41) // Aktk
            return false;

        if (header.Version != 1)
            return false;

        if (header.Reserved != 0)
            return false;

        if (header.Encrypted is not (0 or 1))
            return false;

        return true;
    }

    private static void GenerateKeyStream(ref Span<byte> context, ref Span<byte> nonce, ref int counter, Span<byte> keyMaterial, Span<byte> nonceMaterial)
    {
        Debug.Assert(context.Length == 0x200, "context.Length == 0x200");
        Debug.Assert(nonce.Length == 12, "nonce.Length == 12");

        var nonceMaterial1 = MemoryMarshal.Cast<byte, uint>(nonceMaterial.Slice((counter % 0xd) | 0x30, 4))[0];
        var nonceMaterial2 = MemoryMarshal.Cast<byte, uint>(nonceMaterial.Slice(counter / 0xd % 0xd, 4))[0];
        var nonceXor1 = MemoryMarshal.Cast<byte, uint>(nonceMaterial.Slice(counter / 0xA9 % 0xd | 0x10, 4))[0];
        var nonceXor2 = MemoryMarshal.Cast<byte, uint>(nonceMaterial.Slice(counter / 0x895 % 0xd | 0x20, 4))[0];

        var rotatedTemp = BitOperations.RotateRight(nonceMaterial1, -(2 * (counter % 0x93e / 0xa9) % 0x1b));
        var rotatedTemp2 = BitOperations.RotateRight(nonceMaterial2, -(3 * (counter / 0x93e) % 0x1b));
        var nonceSeed = rotatedTemp ^ rotatedTemp2;

        var nonceValues = MemoryMarshal.Cast<byte, uint>(nonce);
        nonceValues[0] = nonceSeed;
        nonceValues[1] = nonceSeed ^ nonceXor1;
        nonceValues[2] = nonceValues[1] ^ nonceXor2;

        var rounds = new[] { 12, 8, 8, 8, 4, 4, 4, 4 };

        var state = (stackalloc byte[0x40]);
        ChaCha20.SetupContext(ref state, keyMaterial, nonce, ++counter);

        for (int i = 0; i < rounds.Length; i++)
        {
            ChaCha20.GenerateKeyMaterial(
                state,
                context.Slice(i * 0x40, 0x40),
                i == 0 ? default : context.Slice(i * 0x40 - 0x40, 0x40),
                rounds[i]);
        }
    }
}

internal readonly struct Header
{
    public const int Size = 0xc;
    public const int HashSize = 0x10;

    public readonly uint Magic;
    public readonly ushort Version;
    public readonly ushort Reserved;
    public readonly uint Encrypted;

    public Header(uint magic, ushort version, ushort reserved, uint encrypted)
    {
        Magic = magic;
        Version = version;
        Reserved = reserved;
        Encrypted = encrypted;
    }
}

internal static class ChaCha20
{
    public static void SetupContext(ref Span<byte> context, Span<byte> key, Span<byte> nonce, int counter)
    {
        Debug.Assert(context.Length == 0x40, "context.Length == 0x40");

        var contextData = MemoryMarshal.Cast<byte, uint>(context);

        var constant = MemoryMarshal.Cast<byte, uint>("expand 32-byte k"u8);
        contextData[0] = constant[0];
        contextData[1] = constant[1];
        contextData[2] = constant[2];
        contextData[3] = constant[3];

        var keyData = MemoryMarshal.Cast<byte, uint>(key);
        contextData[4] = keyData[0];
        contextData[5] = keyData[1];
        contextData[6] = keyData[2];
        contextData[7] = keyData[3];
        contextData[8] = keyData[4];
        contextData[9] = keyData[5];
        contextData[10] = keyData[6];
        contextData[11] = keyData[7];

        var nonceData = MemoryMarshal.Cast<byte, uint>(nonce);
        contextData[12] = (uint)counter;
        contextData[13] = nonceData[0];
        contextData[14] = nonceData[1];
        contextData[15] = nonceData[2];
    }

    public static void GenerateKeyMaterial(Span<byte> context, Span<byte> keyStream, Span<byte> initialState, int rounds)
    {
        var x = (stackalloc uint[16]);
        var y = (stackalloc uint[16]);

        var contextData = MemoryMarshal.Cast<byte, uint>(context);
        var initialStateData = MemoryMarshal.Cast<byte, uint>(initialState);
        if (initialState != default)
        {
            for (int i = 0; i < 16; i++)
            {
                x[i] = y[i] = contextData[i] ^ initialStateData[i];
            }
        }
        else
        {
            for (int i = 0; i < 16; i++)
            {
                x[i] = y[i] = contextData[i];
            }
        }

        for (int i = rounds; i > 0; i -= 2)
        {
            QuarterRound(ref x, 0, 4, 8, 12);
            QuarterRound(ref x, 1, 5, 9, 13);
            QuarterRound(ref x, 2, 6, 10, 14);
            QuarterRound(ref x, 3, 7, 11, 15);

            QuarterRound(ref x, 0, 5, 10, 15);
            QuarterRound(ref x, 1, 6, 11, 12);
            QuarterRound(ref x, 2, 7, 8, 13);
            QuarterRound(ref x, 3, 4, 9, 14);
        }

        var keyStreamData = MemoryMarshal.Cast<byte, uint>(keyStream);
        for (int i = 0; i < 16; i++)
        {
            keyStreamData[i] = x[i] + y[i];
        }

        contextData[12]++;
        if (contextData[12] <= 0)
        {
            contextData[13]++;
        }
    }

    private static void QuarterRound(ref Span<uint> x, int a, int b, int c, int d)
    {
        Debug.Assert(x != null && x.Length == 16, "x != null && x.Length == 16");

        x[a] += x[b];
        x[d] = BitOperations.RotateLeft(x[d] ^ x[a], 16);
        x[c] += x[d];
        x[b] = BitOperations.RotateLeft(x[b] ^ x[c], 12);
        x[a] += x[b];
        x[d] = BitOperations.RotateLeft(x[d] ^ x[a], 8);
        x[c] += x[d];
        x[b] = BitOperations.RotateLeft(x[b] ^ x[c], 7);
    }
}