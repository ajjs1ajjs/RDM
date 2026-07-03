use argon2::Argon2;
use aes_gcm::{
    aead::{Aead, KeyInit},
    Aes256Gcm, Nonce,
};
use rand::{rngs::OsRng, RngCore};
use serde::{Deserialize, Serialize};

#[derive(Clone, Serialize, Deserialize)]
pub struct EncryptedData {
    pub ciphertext: String, // Hex-encoded
    pub nonce: String,      // Hex-encoded
}

/// Derives a 256-bit Key Encryption Key (KEK) from a master password and salt using PBKDF2-HMAC-SHA256 with 100,000 iterations.
pub fn derive_key(password: &str, salt: &[u8]) -> Result<[u8; 32], String> {
    let mut key = [0u8; 32];
    pbkdf2::pbkdf2::<hmac::Hmac<sha2::Sha256>>(password.as_bytes(), salt, 100000, &mut key);
    Ok(key)
}

/// Encrypts the plaintext using AES-256-GCM and the derived key.
pub fn encrypt_secret(key: &[u8; 32], plaintext: &str) -> Result<EncryptedData, String> {
    let cipher = Aes256Gcm::new_from_slice(key)
        .map_err(|e| format!("Cipher initialization error: {}", e))?;
    
    let mut nonce_bytes = [0u8; 12]; // 12-byte nonce for AES-GCM
    OsRng.fill_bytes(&mut nonce_bytes);
    let nonce = Nonce::from_slice(&nonce_bytes);

    let ciphertext = cipher
        .encrypt(nonce, plaintext.as_bytes())
        .map_err(|e| format!("Encryption error: {}", e))?;

    Ok(EncryptedData {
        ciphertext: hex::encode(ciphertext),
        nonce: hex::encode(nonce_bytes),
    })
}

/// Decrypts the ciphertext using AES-256-GCM and the derived key.
pub fn decrypt_secret(key: &[u8; 32], encrypted: &EncryptedData) -> Result<String, String> {
    let cipher = Aes256Gcm::new_from_slice(key)
        .map_err(|e| format!("Cipher initialization error: {}", e))?;

    let ciphertext_bytes = hex::decode(&encrypted.ciphertext)
        .map_err(|e| format!("Invalid ciphertext hex: {}", e))?;
    
    let nonce_bytes = hex::decode(&encrypted.nonce)
        .map_err(|e| format!("Invalid nonce hex: {}", e))?;

    if nonce_bytes.len() != 12 {
        return Err("Invalid nonce length (must be 12 bytes)".to_string());
    }

    let nonce = Nonce::from_slice(&nonce_bytes);

    let decrypted_bytes = cipher
        .decrypt(nonce, ciphertext_bytes.as_slice())
        .map_err(|e| format!("Decryption error: (incorrect master password?) {}", e))?;

    String::from_utf8(decrypted_bytes)
        .map_err(|e| format!("Decrypted data is not valid UTF-8: {}", e))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encryption_decryption() {
        let password = "my_super_secure_master_password";
        let salt = b"salt_must_be_long_enough_16_bytes"; // At least 16 bytes for Argon2
        
        let key = derive_key(password, salt).unwrap();
        let secret = "my_super_secret_ssh_key_or_password";

        let encrypted = encrypt_secret(&key, secret).unwrap();
        let decrypted = decrypt_secret(&key, &encrypted).unwrap();

        assert_eq!(secret, decrypted);
    }
}
