using System.Security.Cryptography;
using System.Text;

namespace AtelierTool;

public static class MasterDataCrypto
{
    public static byte[] DecryptMasterData(byte[] input, string version)
    {
        var hsh = SHA256.HashData(Encoding.UTF8.GetBytes($"wTmkW6hwnA6HXnItdXjZp/BSOdPuh2L9QzdM3bx1e54={version}"));

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = hsh[..16];

        var dec = aes.DecryptCbc(input, hsh[16..]);
        return dec;
    }
}