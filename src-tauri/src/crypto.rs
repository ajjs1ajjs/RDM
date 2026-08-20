use aes_gcm::{
    aead::{Aead, KeyInit},
    Aes256Gcm, Nonce,
};
use rand::{rngs::OsRng, RngCore};
use serde::{Deserialize, Serialize};

/// Sentinel string used to verify the master password is correct.
/// Stored encrypted with the KEK — no password hash is ever written to disk.
pub const AUTH_SENTINEL: &str = "rdm-auth-sentinel";

#[derive(Clone, Serialize, Deserialize)]
pub struct EncryptedData {
    pub ciphertext: String, // Hex-encoded
    pub nonce: String,      // Hex-encoded
}

/// Derives a 256-bit Key Encryption Key (KEK) from a master password and salt
/// using PBKDF2-HMAC-SHA256 with 600,000 iterations (OWASP-recommended).
pub fn derive_key(password: &str, salt: &[u8]) -> Result<[u8; 32], String> {
    let mut key = [0u8; 32];
    let _ = pbkdf2::pbkdf2::<hmac::Hmac<sha2::Sha256>>(password.as_bytes(), salt, 600_000, &mut key);
    Ok(key)
}

/// Legacy PBKDF2 with 100,000 iterations. Used ONLY to detect/migrate vaults
/// that were created by older versions with the historical iteration count.
pub fn derive_key_legacy(password: &str, salt: &[u8]) -> Result<[u8; 32], String> {
    let mut key = [0u8; 32];
    let _ = pbkdf2::pbkdf2::<hmac::Hmac<sha2::Sha256>>(password.as_bytes(), salt, 100_000, &mut key);
    Ok(key)
}

/// Generates a fresh random 256-bit KEK from the OS CSPRNG.
pub fn generate_random_kek() -> [u8; 32] {
    let mut key = [0u8; 32];
    OsRng.fill_bytes(&mut key);
    key
}

/// Recovers the raw KEK from a DPAPI-protected blob (legacy Windows vaults).
pub fn unprotect_kek(blob: &[u8]) -> Result<[u8; 32], String> {
    use windows::Win32::Foundation::{LocalFree, HLOCAL};
    use windows::Win32::Security::Cryptography::{
        CryptUnprotectData, CRYPTPROTECT_UI_FORBIDDEN, CRYPT_INTEGER_BLOB,
    };

    unsafe {
        let input = CRYPT_INTEGER_BLOB {
            cbData: blob.len() as u32,
            pbData: blob.as_ptr() as *mut u8,
        };
        let mut out = CRYPT_INTEGER_BLOB {
            cbData: 0,
            pbData: std::ptr::null_mut(),
        };

        CryptUnprotectData(
            &input,
            None,
            None,
            None,
            None,
            CRYPTPROTECT_UI_FORBIDDEN,
            &mut out,
        )
        .map_err(|_| {
            "Failed to unprotect vault key with DPAPI (vault may belong to another user/machine)"
                .to_string()
        })?;

        if out.cbData as usize != 32 {
            let _ = LocalFree(HLOCAL(out.pbData as *mut core::ffi::c_void));
            return Err("Unexpected DPAPI key size".to_string());
        }

        let mut key = [0u8; 32];
        std::ptr::copy_nonoverlapping(out.pbData, key.as_mut_ptr(), 32);
        let _ = LocalFree(HLOCAL(out.pbData as *mut core::ffi::c_void));
        Ok(key)
    }
}

/// Service/account identifiers for the OS keyring entry that holds the raw KEK.
pub const KEYRING_SERVICE: &str = "RDM-Manager";
pub const KEYRING_ACCOUNT: &str = "vault-kek";

/// Stores the raw KEK in the OS credential store:
/// Windows Credential Manager, macOS Keychain, or the Linux Secret Service.
pub fn store_kek_in_keyring(kek: &[u8; 32]) -> Result<(), String> {
    let entry = keyring::Entry::new(KEYRING_SERVICE, KEYRING_ACCOUNT)
        .map_err(|e| format!("Failed to access the OS keyring: {}", e))?;
    entry
        .set_secret(kek)
        .map_err(|e| format!("Failed to store the vault key in the OS keyring: {}", e))
}

/// Recovers the raw KEK from the OS credential store.
pub fn get_kek_from_keyring() -> Result<[u8; 32], String> {
    let entry = keyring::Entry::new(KEYRING_SERVICE, KEYRING_ACCOUNT)
        .map_err(|e| format!("Failed to access the OS keyring: {}", e))?;
    let secret = entry
        .get_secret()
        .map_err(|e| format!("Vault key not found in the OS keyring: {}", e))?;
    if secret.len() != 32 {
        return Err("Unexpected vault key size in the OS keyring".to_string());
    }
    let mut key = [0u8; 32];
    key.copy_from_slice(&secret);
    Ok(key)
}

/// Removes the KEK entry from the OS credential store.
pub fn remove_kek_from_keyring() -> Result<(), String> {
    let entry = keyring::Entry::new(KEYRING_SERVICE, KEYRING_ACCOUNT)
        .map_err(|e| format!("Failed to access the OS keyring: {}", e))?;
    entry
        .delete_credential()
        .map_err(|e| format!("Failed to remove the vault key from the OS keyring: {}", e))
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

    let ciphertext_bytes =
        hex::decode(&encrypted.ciphertext).map_err(|e| format!("Invalid ciphertext hex: {}", e))?;

    let nonce_bytes =
        hex::decode(&encrypted.nonce).map_err(|e| format!("Invalid nonce hex: {}", e))?;

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
