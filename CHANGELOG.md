# Changelog

## v0.1.49

- Added MSI installer support (WiX)
- Added auto-update check via GitHub API (checks latest release on startup)
- Removed master password prompt on login — vault auto-unlocks with default key
- Password prompt only for export/import operations
- Added `scripts/release.ps1` for publishing GitHub releases
- Added update notification banner in UI
- Version sync across all config files
