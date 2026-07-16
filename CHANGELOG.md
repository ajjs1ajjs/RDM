# Changelog

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
