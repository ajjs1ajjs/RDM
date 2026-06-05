using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace RemoteManager.Helpers;

public static class CryptoHelper
{
    private const int SaltSize = 32;
    private const int IvSize = 16;
    private const int AesKeySize = 32;
    private const int Argon2Iterations = 4;
    private const int Argon2MemorySize = 65536; // 64 MB
    private const int Argon2DegreeOfParallelism = 2;

    private static byte[] DeriveKey(string password, byte[] salt, int keySize)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = Argon2Iterations,
            MemorySize = Argon2MemorySize,
            DegreeOfParallelism = Argon2DegreeOfParallelism
        };
        return argon2.GetBytes(keySize);
    }

    public static byte[] ProtectAes(byte[] plainText, string password)
    {
        var salt = new byte[SaltSize];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(salt);

        var iv = new byte[IvSize];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(iv);

        var key = DeriveKey(password, salt, AesKeySize);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        ms.Write(salt, 0, salt.Length);
        ms.Write(iv, 0, iv.Length);
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(plainText, 0, plainText.Length);
        }
        return ms.ToArray();
    }

    public static byte[] UnprotectAes(byte[] cipherData, string password)
    {
        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        Buffer.BlockCopy(cipherData, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(cipherData, SaltSize, iv, 0, IvSize);

        var encryptedBytes = new byte[cipherData.Length - SaltSize - IvSize];
        Buffer.BlockCopy(cipherData, SaltSize + IvSize, encryptedBytes, 0, encryptedBytes.Length);

        var key = DeriveKey(password, salt, AesKeySize);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(encryptedBytes);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var msOut = new MemoryStream();
        cs.CopyTo(msOut);
        return msOut.ToArray();
    }

    public static string HashPassword(string password)
    {
        var salt = new byte[SaltSize];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(salt);

        var hash = DeriveKey(password, salt, 32);

        var result = new byte[SaltSize + hash.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(hash, 0, result, SaltSize, hash.Length);
        return Convert.ToBase64String(result);
    }

    public static bool VerifyPassword(string password, string hashedPassword)
    {
        var data = Convert.FromBase64String(hashedPassword);
        if (data.Length < SaltSize + 1)
            return false;

        var salt = new byte[SaltSize];
        Buffer.BlockCopy(data, 0, salt, 0, SaltSize);

        var storedHash = new byte[data.Length - SaltSize];
        Buffer.BlockCopy(data, SaltSize, storedHash, 0, storedHash.Length);

        var computedHash = DeriveKey(password, salt, storedHash.Length);

        return CryptographicOperations.FixedTimeEquals(storedHash, computedHash);
    }
}
