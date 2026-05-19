using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RemoteManager.Helpers;

public static class EncryptionHelper
{
    private const int Keysize = 256;
    private const int DerivationIterations = 10000;
    private const int SaltSize = 16;
    private const int IvSize = 16;

    public static byte[] Encrypt(string plainText, string password)
    {
        // Generate salt and IV
        var salt = new byte[SaltSize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        var iv = new byte[IvSize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(iv);
        }

        var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

        using var passwordDerivation = new Rfc2898DeriveBytes(password, salt, DerivationIterations, HashAlgorithmName.SHA256);
        var keyBytes = passwordDerivation.GetBytes(Keysize / 8);

        using var symmetricKey = Aes.Create();
        symmetricKey.BlockSize = 128;
        symmetricKey.Mode = CipherMode.CBC;
        symmetricKey.Padding = PaddingMode.PKCS7;

        using var encryptor = symmetricKey.CreateEncryptor(keyBytes, iv);
        using var memoryStream = new MemoryStream();
        
        // Write Salt and IV at the beginning of the file
        memoryStream.Write(salt, 0, salt.Length);
        memoryStream.Write(iv, 0, iv.Length);

        using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
        {
            cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
            cryptoStream.FlushFinalBlock();
        }

        return memoryStream.ToArray();
    }

    public static string Decrypt(byte[] cipherTextBytes, string password)
    {
        if (cipherTextBytes == null || cipherTextBytes.Length < SaltSize + IvSize)
            throw new CryptographicException("Invalid backup file format.");

        // Read Salt and IV from the beginning
        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        
        System.Buffer.BlockCopy(cipherTextBytes, 0, salt, 0, SaltSize);
        System.Buffer.BlockCopy(cipherTextBytes, SaltSize, iv, 0, IvSize);

        var encryptedBytes = new byte[cipherTextBytes.Length - SaltSize - IvSize];
        System.Buffer.BlockCopy(cipherTextBytes, SaltSize + IvSize, encryptedBytes, 0, encryptedBytes.Length);

        using var passwordDerivation = new Rfc2898DeriveBytes(password, salt, DerivationIterations, HashAlgorithmName.SHA256);
        var keyBytes = passwordDerivation.GetBytes(Keysize / 8);

        using var symmetricKey = Aes.Create();
        symmetricKey.BlockSize = 128;
        symmetricKey.Mode = CipherMode.CBC;
        symmetricKey.Padding = PaddingMode.PKCS7;

        using var decryptor = symmetricKey.CreateDecryptor(keyBytes, iv);
        using var memoryStream = new MemoryStream(encryptedBytes);
        using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
        using var reader = new StreamReader(cryptoStream, Encoding.UTF8);
        
        return reader.ReadToEnd();
    }
}
