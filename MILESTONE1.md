# Milestone 1: per-user storage and unelevated startup

Implemented in the working tree on 2026-09-21, based on commit `edbd4d9116f10597e9c16934df783be9728a98ab`. This completes the first hardening milestone. The drag-to-group organizer is the accepted next product direction in `ROADMAP.md`; it is not implemented in this change.

## Behavior

Configuration, group images, icon caches, generated links, JIT profiles, migration receipts, and logs now live under `%LOCALAPPDATA%\TaskbarGroups`. The executable path is separate from data paths. Manager, editor, popup, icon loading, and generated shortcuts no longer depend on the current working directory for application state. New shortcut working directories default to the installation directory rather than the EXE filename.

The application no longer creates `directoryTestingDocument.txt`, requests `runas`, or kills/relaunches itself. Its compiled manifest explicitly requests `asInvoker`. This preserves the caller's context; it does not prevent a user from deliberately starting the program as administrator.

Unavailable startup storage returns exit code 1 without a dialog, elevation request, or focus-stealing error window. Diagnostics normally go to `%LOCALAPPDATA%\TaskbarGroups\Logs\Storage.log`. If that folder cannot be written, the best-effort fallback is `%LOCALAPPDATA%\TaskbarGroups-Startup.log`, then diagnostic tracing if even the profile is unavailable. Fix access/free space and relaunch to retry. Existing read-only state is detected by the normal startup log write.

## Legacy migration

On launch, the program checks `config/` beside its executable. It copies each valid group into staging under the user-data root, validates the copied XML and group images, then publishes the complete directory by a same-volume move. It regenerates missing group links using the actual EXE path, user-data icon path, existing group argument, and unchanged AppUserModelID scheme.

- Original group directories and old links are never deleted or modified.
- Existing user-data groups and links take precedence and are never overwritten by migration.
- Per-source-group receipts make repeated launches idempotent and prevent deleted migrated groups from being resurrected.
- A directory published before an interrupted link/receipt step is retained; the next launch can complete the missing work.
- Malformed XML, invalid group metadata, unreadable/corrupt group images, and linked/reparse-point sources are skipped and logged. The manager shows a nonmodal status line when imports were skipped. Correct the originals and relaunch to retry uncompleted imports.
- A bounded file lock serializes import attempts. If another import cannot finish in time, the caller logs the deferral and tries again on a future launch.
- Existing JIT profiles are not migrated: they are disposable runtime cache files and regenerate in user storage.

Keep the replacement EXE at the existing installation path if old pinned links must continue pointing to it. Preserving names/AppUserModelIDs does not retarget a pin whose executable was moved elsewhere. Windows pinning itself was not altered by this change. Retain legacy files until satisfied with the migration; removing them manually may also remove icons still referenced by old pins.

This is staged **migration**, not yet transactional ordinary group editing. The legacy editor's delete-then-create save behavior, broken icon-cache loop, broader launch handling, and other failure-isolation issues remain open for subsequent milestones.

## Implementation map

- `main/Classes/MainPath.cs`: central paths, group path validation, user folders, warnings/logging.
- `main/Classes/LegacyMigration.cs`: validation, copy, publication, import receipts, retry and conflict preservation.
- `main/client.cs`: production entry delegates to the testable startup routine with the actual EXE and Windows LocalApplicationData path; quiet storage failures.
- `main/Properties/app.manifest`: explicitly unelevated default.
- Category/forms/controls: state-path and shortcut-path call sites updated; editor name validation precedes destructive save work.
- `verification/StorageProbe.cs`: isolated fixtures and regression probes. `verification/Probe.cs` remains historical baseline-only.

## Verification

Release compilation on `dockerbox` (`HostPC`), VS 2022/MSBuild 17.14, `/m:2`: **0 warnings, 0 errors**. The compiled manifest was extracted with Windows SDK `mt.exe` and confirmed to contain `requestedExecutionLevel level="asInvoker" uiAccess="false"`. No automatic elevation/relaunch code remains in `main/`.

Private-desktop verification on Windows 11 Pro 25H2 build 26200.9445: **29 checks passed, 0 unexpected failures**.

Coverage includes:

- Actual Windows ACL denial of installation-folder writes; source-tree content unchanged after migration and startup.
- Byte-for-byte valid group/cache import; malformed XML and corrupt images isolated.
- COM `.lnk` readback confirms target, legacy argument, working directory, and per-user icon location.
- New group creation and migrated image loading from an unrelated working directory.
- Existing destination precedence, retained newer data, idempotent retry, recovery from the post-directory-publication state, and no resurrection after deletion.
- Manager and legacy-name popup opened and closed cleanly through the production startup routine.
- Blocked and read-only user storage exited without UI, produced diagnostics, and recovered after repair.
- Path traversal/outside-root reads rejected; missing/unimported group startup exited quietly.

Tests inject disposable EXE/profile paths into the internal startup routine; there is no production command-line or environment override for the data directory. The normal entry point supplies Windows' LocalApplicationData folder. This prevents tests from importing fixtures into the user's real profile. Tests did not exercise actual Explorer pinning, real UWP/URI targets, multiple monitors, or the new organizer gestures. All temporary ACL changes were restored; no test processes remain running.

## Build and artifacts

Remote source: `C:\Sandbox\Codex\Workspaces\taskbar-groups`.
Intermediate output: `C:\Sandbox\Codex\Builds\taskbar-groups\Milestone1`.
Artifacts: `C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone1`.
Build log: `C:\Sandbox\Codex\Logs\taskbar-groups\Milestone1.log` (local `out/Milestone1.log`). Existing NuGet packages and incremental state were retained.

Use the baseline MSBuild command in `VERIFICATION.md` with the milestone output/intermediate paths above. Build the test harness on the worker:

```powershell
$Output = 'C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone1'
& C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /platform:x64 `
    /out:$Output\StorageProbe.exe /r:$Output\TaskbarGroups.exe `
    /r:System.Drawing.dll /r:System.Windows.Forms.dll .\verification\StorageProbe.cs
```

Transfer the runtime files plus harness into a fresh disposable directory. Start only `StorageProbe.exe` using `Start-Process -WindowStyle Hidden -PassThru`, wait with a bounded timeout, then inspect `Verification.txt` and the exit code. The harness creates and verifies a private desktop; it never switches the input desktop. It temporarily denies writes on its own fixture folders and restores their permissions in `finally` blocks.

Clean deliverable: `out/TaskbarGroups-Milestone1.zip`, containing the application and dependencies, without the harness or fixture data. Final test log: `out/Milestone1-Verified/Verification.txt`; portable summary in `verification/Milestone1Results.txt`. No real Taskbar Groups profile data, pins, or Explorer state was changed during verification.
