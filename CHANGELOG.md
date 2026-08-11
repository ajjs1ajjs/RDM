# Changelog

## v0.1.75

- Revert page-scroll behavior; SSH terminal now always scrolls to bottom (fixes duplicated prompts when pasting via context menu)

## v0.1.74

- SSH terminal page-scroll: when the cursor reaches near the bottom, the viewport jumps so the cursor sits a few rows below the top (new output starts from the top, history stays in the scrollbar)

## v0.1.73

- Fix terminal-pad sizing: use absolute positioning (`top/bottom/left/right`) instead of `height:100%` inside a flex item so FitAddon always measures the exact renderable area (fixes prompt hiding below the viewport)

## v0.1.72

- Fix SSH terminal paste: paste now uses the DOM `paste` event (clipboardData) directly instead of the permission-gated async clipboard API; the key handler no longer blocks the browser paste event
- Fix SSH terminal auto-scroll: viewport is now pinned to the latest output after the whole write queue is processed (`onWriteParsed`) plus `scrollOnUserInput`, so the prompt/cursor stays visible when output fills the screen

## v0.1.71

- Fix SSH terminal auto-scroll when output reaches the bottom: terminal now sizes to the exact visible area (padding moved to a wrapper so FitAddon measures the renderable region) and keeps the prompt/cursor in view

## v0.1.70

- Fix SSH connections closing immediately: revert `StrictHostKeyChecking` to `accept-new` (the `yes` setting refused any host not yet in `known_hosts`)
- Show SSH client errors (e.g. "Host key verification failed") in the terminal before the session closes instead of closing silently

## v0.1.69

- Fix "wrong password" on importing backups / unlocking legacy vaults after the PBKDF2 upgrade: derivation now falls back to the legacy 100k iteration scheme for data created before v0.1.68 (600k current + 100k legacy detection)
- Bump version to v0.1.69

## v0.1.68

- PBKDF2 iteration count for backup passwords raised to 600,000 (legacy 100k kept only for old-vault detection)
- SSH passwords/passphrases are now zeroized in memory (Zeroizing) instead of lingering as plaintext
- Temporary `.rdp` session files are cleaned up (startup purge + per-session removal)
- Optimized credential/server lookups to indexed by-id SQL queries (removed full-table scans on connect/edit/decrypt)
- Fixed high-severity npm advisory (postcss/nanoid, build-time dependency)

## v0.1.67

- Security hardening per audit:
  - Vault KEK is now a random key protected with Windows DPAPI (no more hardcoded `default_rdm_key`); legacy vaults auto-migrate on startup
  - Removed automatic disabling of RDP certificate validation
  - Redacted password from RdpHost log
  - Async update check (no UI freeze)
  - Transaction-safe re-encryption and atomic backup export/import
  - CSV import no longer leaks passwords in error text
  - SSH/SFTP no longer delete active sessions' temp keys
  - Terminal escape-sequence injection sanitized
  - RDP credentials use session persistence + cleanup on disconnect/exit
  - StrictHostKeyChecking=yes for interactive SSH
  - Removed dead code and unused dependencies

## v0.1.66

- Release rebuild of v0.1.65 with identical changes (installer/portable bundle verification)

## v0.1.65

- Explicitly bundle `WebView2Loader.dll` in installer resources and portable packages to fix system error "WebView2Loader.dll was not found"

## v0.1.64

- Add NSIS setup installer target (`_setup.exe`) to prevent WiX MSI UAC elevation hangs during installation/update
- Clean build bundle: NSIS installer (`.exe`), MSI (`.msi`), and Portable (`.zip`)

## v0.1.63

- Fix SSH terminal bottom line clipping & auto-scroll to keep active prompt visible
- Add ResizeObserver for dynamic terminal container fitting on tab switch & window resize
- Fix multi-language layout copy/paste hotkeys (Ukrainian Ctrl+С / Ctrl+М & English layout support)
- Add auto-copy on text selection in SSH terminal

## v0.1.62

- Fix SSH terminal copy & paste (Ctrl+C, Ctrl+V, Ctrl+Shift+C/V, Cmd+C/V, Shift/Ctrl+Insert)
- Add selection detection and right-click context menu in SSH terminal (Copy, Paste, Select All, Clear)

## v0.1.59

- Reset Vault now wipes ALL data (servers, credentials, history, settings) for a completely clean start

## v0.1.58

- Fix Windows Credential Manager: password blob must be UTF-16LE for mstsc
- RDP credentials use session persistence (CRED_PERSIST_SESSION) — credentials
  are removed when the user logs off, so they are not stored across reboots.
  This is intentional for security. To persist across reboots, the value must
  be CRED_PERSIST_LOCAL_MACHINE (2); the shipped build keeps session-only.

## v0.1.57

- Fix RDP NLA authentication error (authentication level + enablecredsspsupport)
- Kill orphaned mstsc.exe on app exit (Drop impl for RdpState)

## v0.1.56

- Make resolve_auth gracefully handle decryption errors (fallback to manual credentials)
- Save manually entered RDP credentials even when server already has linked credential

## v0.1.55

- Fix vault reset now also clears encrypted credentials/passwords (old KEK is gone)

## v0.1.54

- Fix vault reset ("Reset Vault") — was setting sentinel to empty string instead of deleting it, causing JSON parse error

## v0.1.53

- Fix Rust compiler warnings (unused ShowWindow return values)
- Code cleanup and formatting

## v0.1.52

- Add vault reset option for users who forgot their master password
- "Reset Vault" button in migration dialog clears vault and reinitializes with default key

## v0.1.51

- Fix vault decryption error for users who previously had a master password
- Add one-time migration dialog to migrate vault to default encryption key
- Verify vault sentinel on startup before auto-unlocking

## v0.1.50

- Clean release: MSI + portable only (no NSIS)
- Auto-update check via GitHub API
- No master password at login — only for export/import
- Update notification banner on new version
