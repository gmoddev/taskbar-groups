# Stable group identity and recoverable saves

Implemented on 2026-09-21 after milestone 1. This completes the **group persistence portion** of roadmap milestone 2. The ordered organizer model, cross-group operations, undo, and drag-to-group UI are still planned.

## Behavior

- New groups and members receive GUID identities. Display names may contain Unicode, punctuation, slashes, or Windows reserved filenames because names no longer determine new storage paths or launcher arguments. Duplicate display names are permitted; identity distinguishes groups.
- Legacy groups retain their original directory as a permanent CLI alias. Their deterministic group/member IDs are persisted when first saved. Their original AppUserModelID suffix remains unchanged; new groups use their GUID suffix. Renames do not change identity, the generated shortcut filename, or the argument in new links.
- The editor works on a deep copy. Save and delete failures appear within the editor and in the storage log, without modal dialogs. Stale editor revisions cannot overwrite or delete a newer save. A bounded, exclusive file lock serializes cooperating writers.
- Corrupt groups are skipped independently in the manager. Recoveries and publication failures appear in its status strip and storage log. Existing launch/error handling elsewhere still needs milestone 3.
- Icon caches use member identity plus a hash of the target path, independently of editor controls. The dead cache loop and basename collisions have been removed. Caches are disposable and outside saved generations.

## Commit and recovery model

Each group has a stable root under `%LOCALAPPDATA%\TaskbarGroups\config`. Newly created roots are GUIDs; upgraded legacy roots retain their original directory names for compatibility.

```text
config/<stable-root>/
    Current.xml                 committed generation pointer or deletion tombstone
    Previous.xml                previous pointer after a replacement
    Damaged.xml                 damaged pointer retained during recovery save
    Versions/<revision-guid>/
        ObjectData.xml          schema version 2, IDs, display metadata, ordered members
        GroupImage.png
        GroupIcon.ico
        Launcher.lnk
        Validation.xml
    Cache/<member-id>-<target-hash>.png
Shortcuts/<group-id>.lnk
```

Saving writes a fresh generation on the same volume, flushes files, validates the XML and image resources, writes and flushes a new pointer, then publishes it with `File.Replace` (or `File.Move` for the first pointer). The pointer is the sole group commit point. Published generation files are never edited. Failures before publication leave the prior group usable; an interrupted first save remains invisible.

The generated link is a recoverable projection of committed data: after commit, it is copied through a temporary file and atomically replaced. Failure here does not undo the saved group. Opening the manager retries publication from the committed generation. Existing pins continue to resolve through stable arguments; old icon generations and legacy resources are retained. This deliberately does not promise immediate Explorer icon-cache refresh.

A damaged current pointer/generation can fall back to the previous complete generation, or retained legacy data on the first upgrade. Recovery is logged; the next successful save preserves the good fallback and quarantines the damaged pointer. Unsupported future schemas are refused instead of silently downgraded.

Deleting a group commits a tombstone. It does not recursively remove resources, manipulate real pins, or delete application targets. Old links to a deleted group fail quietly. Retained legacy import receipts and tombstones prevent normal restart from resurrecting deleted groups.

## Verification

Release build on `dockerbox` / `HostPC`, Visual Studio MSBuild 17.14, `/m:2`: **0 warnings, 0 errors**. NuGet packages and incremental outputs were reused. No toolchain installation or persistent worker process was required.

Two independent disposable fixtures ran locally on private Windows desktops without switching the input desktop:

- **45 persistence checks:** legacy aliases and GUID resolution after rename; Unicode/path-like names; stable member IDs and order; isolated editor copies; separate same-basename icon caches; stale-save/delete conflicts; writer lock contention; read-only pointer replacement failure; invalid-surrogate serialization failure; recovery from damaged pointers; future-schema refusal; durable deletion; corrupt-group isolation; invisible uncommitted creation; manager and legacy/GUID popup smoke checks.
- The persistence harness exits a child process abruptly at six stages: XML flushed, images written, resources flushed, generation validated, pointer flushed, and pointer committed. Reopening sees the complete old generation before commit and the complete new generation afterward. Previous generation bytes remain intact. This is process-interruption testing, not a physical power-loss/filesystem durability certification.
- **29 storage regression checks:** protected installation ACLs, migration preservation/retries, unrelated working directories, shell-link readback, manager/popup smoke checks, blocked/read-only profile handling and recovery, traversal rejection, and quiet missing-group startup. The harness was updated only where GUID storage changed the expected new-group paths/arguments.

Combined: **74 passed, 0 unexpected failures**. Application targets were not launched by these suites. No real profile, taskbar pins, Explorer settings, or input desktop was changed. Temporary ACLs were restored.

Source harnesses: `verification/PersistenceProbe.cs` and `verification/StorageProbe.cs`. Logs: `verification/Milestone2Results.txt`, copied from `out/Milestone2-Final/Verification.txt` and `out/Milestone2-StorageFinal/Verification.txt`. The older `verification/Probe.cs` remains baseline-only.

## Build and artifacts

- Remote source: `C:\Sandbox\Codex\Workspaces\taskbar-groups`.
- Intermediates: `C:\Sandbox\Codex\Builds\taskbar-groups\Milestone2`.
- Runtime output: `C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone2`.
- Build log: `C:\Sandbox\Codex\Logs\taskbar-groups\Milestone2.log`, copied to `out/Milestone2.log`.
- Clean package: `out/TaskbarGroups-Milestone2a.zip`. Harnesses and fixtures are excluded.
- EXE SHA256: `CEB3B75245C28EFAE2F6C4F24668D36E5EEA5C08366A5912FD6751B82CECFBEE`.

Use the MSBuild command from `VERIFICATION.md` with the milestone output/intermediate paths above. Compile each harness as in `MILESTONE1.md`, replacing `StorageProbe` with `PersistenceProbe` for the persistence suite. Run each in its own fresh disposable output folder using `Start-Process -WindowStyle Hidden`, a bounded wait, and inspection of `Verification.txt` and the exit code. Do not run either harness in an installation or real profile.

## Remaining work and limits

The group schema preserves member order, but there is no ordered model spanning standalone items and multiple groups yet. Atomic multi-group moves, operation history, undo, and the requested drag/drop UI remain the next foundation work. Do not mark all of milestone 2 complete.

Old generations, incomplete staging files, tombstones, and old generated links are retained deliberately. A bounded retention/cleanup policy and user-facing restoration UI remain to be implemented. Avoid downgrading to the milestone 1 executable after editing versioned groups: it does not understand the pointer format. Keep originals/backups, but use the current executable to read current state.

Actual Explorer pin activation/icon refresh, Windows 10, monitor/DPI behavior, packaged-app/URI launch handling, broad native resource ownership, and power-loss durability remain unverified. Per-item malformed metadata is still handled at the group boundary rather than salvaging individual healthy members. The .NET 10 migration remains separate.
