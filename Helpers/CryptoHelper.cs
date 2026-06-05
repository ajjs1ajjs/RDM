using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RemoteManager.Helpers;

public static class CryptoHelper
{
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("RDM_MasterSalt_123");

    public static byte[] ProtectAes(byte[] plainText, string password)
    {
        using var rfc2898 = new Rfc2898DeriveBytes(password, Salt, 10000, HashAlgorithmName.SHA256);
        byte[] key = rfc2898.GetBytes(32);
        byte[] iv = rfc2898.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(plainText, 0, plainText.Length);
        }
        return ms.ToArray();
    }

    public static byte[] UnprotectAes(byte[] cipherText, string password)
    {
        using var rfc2898 = new Rfc2898DeriveBytes(password, Salt, 10000, HashAlgorithmName.SHA256);
        byte[] key = rfc2898.GetBytes(32);
        byte[] iv = rfc2898.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(cipherText);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var msOut = new MemoryStream();
        cs.CopyTo(msOut);
        return msOut.ToArray();
    }

    public static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "RDM_HASH_SALT"));
        return Convert.ToBase64String(hash);
    }
}
