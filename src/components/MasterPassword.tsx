import React, { useState, useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";
import { ShieldAlert, LockKeyhole } from "lucide-react";

interface MasterPasswordProps {
  onUnlock: () => void;
}

export const MasterPassword: React.FC<MasterPasswordProps> = ({ onUnlock }) => {
  const [isSetup, setIsSetup] = useState<boolean>(false);
  const [password, setPassword] = useState<string>("");
  const [confirmPassword, setConfirmPassword] = useState<string>("");
  const [error, setError] = useState<string>("");
  const [loading, setLoading] = useState<boolean>(false);

  useEffect(() => {
    checkVaultSetup();
  }, []);

  const checkVaultSetup = async () => {
    try {
      const setup = await invoke<boolean>("is_vault_setup");
      setIsSetup(setup);
    } catch (err) {
      console.error(err);
      setError("Failed to connect to database backend");
    }
  };

  const handleSetup = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!password) return;
    if (password !== confirmPassword) {
      setError("Passwords do not match");
      return;
    }
    if (password.length < 8) {
      setError("Password must be at least 8 characters");
      return;
    }

    setLoading(true);
    setError("");
    try {
      await invoke("setup_master_password", { password });
      setPassword("");
      onUnlock();
    } catch (err: any) {
      setError(err.toString());
    } finally {
      setLoading(false);
    }
  };

  const handleUnlock = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!password) return;

    setLoading(true);
    setError("");
    try {
      const success = await invoke<boolean>("unlock_vault", { password });
      if (success) {
        setPassword("");
        onUnlock();
      } else {
        setError("Invalid master password");
      }
    } catch (err: any) {
      setError(err.toString());
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="lock-screen">
      <div className="lock-card glass-card">
        <div className="lock-logo">
          {isSetup ? (
            <LockKeyhole size={48} className="logo-icon" />
          ) : (
            <ShieldAlert size={48} className="logo-icon" style={{ color: "var(--accent-warn)" }} />
          )}
        </div>

        <h1 className="lock-title">RDM Manager</h1>
        <p className="lock-subtitle">
          {isSetup
            ? "Enter master password to decrypt vault"
            : "Set a strong master password to secure your server connections"}
        </p>

        {error && (
          <div
            style={{
              padding: "10px",
              backgroundColor: "rgba(255, 69, 58, 0.1)",
              border: "1px solid rgba(255, 69, 58, 0.3)",
              borderRadius: "6px",
              color: "var(--accent-red)",
              fontSize: "0.85rem",
              marginBottom: "20px",
            }}
          >
            {error}
          </div>
        )}

        <form onSubmit={isSetup ? handleUnlock : handleSetup} className="lock-form">
          <div className="input-group">
            <input
              type="password"
              className="text-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••••••"
              disabled={loading}
              autoFocus
            />
          </div>

          {!isSetup && (
            <div className="input-group">
              <input
                type="password"
                className="text-input"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                placeholder="••••••••••••"
                disabled={loading}
              />
            </div>
          )}

          <button type="submit" className="btn btn-primary" style={{ justifyContent: "center", marginTop: "10px" }} disabled={loading}>
            {loading ? "Decrypting..." : isSetup ? "Unlock Vault" : "Initialize Vault"}
          </button>
        </form>
      </div>
    </div>
  );
};
