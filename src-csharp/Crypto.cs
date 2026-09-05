using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace RDM
{
    public class EncryptedData
    {
        public string ciphertext { get; set; } = "";
        public string nonce { get; set; } = "";
    }

    public static class Crypto
    {
        /// <summary>
        /// Derives a 256-bit Key Encryption Key (KEK) from a master password and salt using PBKDF2-HMAC-SHA256 with 100,000 iterations.
        /// </summary>
        public static byte[] DeriveKey(string password, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                100000,
                HashAlgorithmName.SHA256,
                32
            );
        }

        /// <summary>
        /// Encrypts the plaintext using AES-256-GCM, returning combined ciphertext + tag and nonce in hex format (matching Rust format).
        /// </summary>
        public static EncryptedData EncryptSecret(byte[] key, string plaintext)
        {
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            byte[] ciphertext = new byte[plaintextBytes.Length];
            byte[] tag = new byte[16];

            using (AesGcm aes = new AesGcm(key, 16))
            {
                aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
            }

            // Combine ciphertext + tag to match Rust's format
            byte[] combined = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

            return new EncryptedData
            {
                ciphertext = Convert.ToHexString(combined).ToLowerInvariant(),
                nonce = Convert.ToHexString(nonce).ToLowerInvariant()
            };
        }

        /// <summary>
        /// Decrypts the ciphertext using AES-256-GCM and the derived key. Handles combined tag format (matching Rust format).
        /// </summary>
        public static string DecryptSecret(byte[] key, EncryptedData encrypted)
        {
            byte[] ciphertextWithTag = Convert.FromHexString(encrypted.ciphertext);
            byte[] nonce = Convert.FromHexString(encrypted.nonce);

            if (nonce.Length != 12)
            {
                throw new ArgumentException("Invalid nonce length (must be 12 bytes)");
            }

            int tagLength = 16;
            if (ciphertextWithTag.Length < tagLength)
            {
                throw new ArgumentException("Invalid ciphertext length (too short)");
            }

            byte[] ciphertext = new byte[ciphertextWithTag.Length - tagLength];
            byte[] tag = new byte[tagLength];

            Buffer.BlockCopy(ciphertextWithTag, 0, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(ciphertextWithTag, ciphertext.Length, tag, 0, tagLength);

            byte[] plaintext = new byte[ciphertext.Length];

            using (AesGcm aes = new AesGcm(key, tagLength))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return Encoding.UTF8.GetString(plaintext);
        }
    }
}
