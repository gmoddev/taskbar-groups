# Roadmap

The product is a compact organizer with immediate drag-to-group and the existing popup launcher. Keep ordinary shell links and AppUserModelID; no Explorer injection, taskbar hooks, services or drivers.

## Implemented

- Per-user state, no automatic elevation, read-only legacy import.
- Stable identities, atomic snapshots, crash recovery, stale-write rejection and durable undo/redo.
- Explicit launch validation and quiet per-item failures.
- Drag grouping/reordering/member removal, automatic dissolution, names/icons/links and inline rename.
- Atomic external imports, pinned shortcut copies and visible running-app discovery; F5 refresh.
- Manual pin guidance and a user-triggered native pin preview.
- Monitor-local popup placement, Release bitness correction and package-root artwork support.

See [HISTORY.md](HISTORY.md) for design decisions and [VERIFICATION.md](VERIFICATION.md) for evidence.

## Next priorities

1. Validate real drag/drop and native per-group pin confirmation interactively, including two distinct group identities and cancellation. Keep tests from modifying the user's taskbar.
2. Improve discovery of closed packaged/system pins and explain incomplete discovery. Add explicit reimport/refresh controls while respecting undo receipts, existing grouping and edited launch metadata.
3. Investigate supported taskbar ordering without hooks. Do not represent organizer movement as real Explorer movement. Windows confirmation remains part of pinning.
4. Maintain owned Start-menu projections across rename, executable relocation and deletion. Avoid overwriting unrelated entries.
5. Add compact optional item details and a deliberate later legacy-data merge workflow without restoring the old editor-first experience.
6. Add safe retention, recovery inspection and selective salvage. Preserve referenced generations, historical identities and undo entries.
7. Complete resource/static analysis and Windows/DPI/auto-hide testing, including packaged/URI/folder activation.
8. Evaluate .NET 10 WinForms migration separately after behavior and deployment compatibility are established.

The latest 393 checks establish isolated behavior, not complete daily-use or Windows shell compatibility.
