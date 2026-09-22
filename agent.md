# Agent instructions

## Scope and conventions

- Work in `gmoddev/taskbar-groups`. Read `aicontext.md` and the verification report before changing code.
- Use PascalCase for new names, including methods, fields, locals, and parameters. Retain framework-required signatures and existing serialized XML names unless a compatibility migration is intentional. Avoid unrelated bulk renaming.
- Name helpers `GetFolder`, `GetObj`, etc., rather than `GetOrCreateFolder` or `GetOrCreateObj`.
- Use log prefixes such as `[TaskbarGroups:Storage]`, `[TaskbarGroups:Launch]`, and `[TaskbarGroups:Icons]`.
- Keep changes small and reviewable. An item in `ROADMAP.md` is planned work, not proof it has been implemented.

## User desktop protection

The user may be gaming. Do not steal focus, display error dialogs, cause a UAC prompt, switch desktops, pin/unpin taskbar items, restart Explorer, or launch arbitrary existing shortcuts during automated verification.

Use disposable state and a private desktop for runtime tests. Never exercise the historical baseline automatic-elevation branch on the user's active desktop. On failure, write a log and return a failing result. Do not fall back to interactive execution when isolation fails. Milestone 1 removes automatic elevation and makes startup storage failures quiet; milestone 3a removes popup launch dialogs. Milestone 3b removes the remaining editor/icon MessageBox and adds shared icon failure boundaries.

New failure handling should keep errors in the application's own nonmodal status area and log, isolate a bad item/group, and preserve the last usable configuration. Only a target application should request elevation for its own work.

## Architecture constraints

- Retain ordinary `.lnk` files, AppUserModelID, and the popup launcher.
- Do not add Explorer injection, global taskbar hooks, taskbar memory manipulation, DLL patching, a persistent service, or a driver.
- Separate executable location from writable state. Target `%LOCALAPPDATA%\TaskbarGroups` for future state, including shortcuts, caches, profiles, and logs.
- Preserve legacy groups and pinned links through explicit migration. Display-name changes must leave stable group/member IDs, legacy aliases, and AppUserModelIDs unchanged. Read HISTORY.md and HISTORY.md before changing versioned storage; never modify published generations or bypass revision checks. After organizer activation, its global snapshot is authoritative; route all readers/writers through it. Activation belongs to frmOrganizer, the default no-argument surface, which displays both standalone items and groups. Popup-only startup must not activate a profile. Read HISTORY.md before changing drag/drop routing.
- Internal drag payloads carry the originating window and revision. Preserve center-versus-edge zones, reject stale/self drops, commit before rendering changed membership and advertise Copy for external file drops. Never move/delete imported target files. Defer menu/editor disposal until their event handlers finish.
- Read HISTORY.md before import changes. External file/shortcut drops must advertise Copy and commit accepted inputs plus grouping as one undoable transaction through OrganizerStore.ImportItems. Never dispatch targets during import. Duplicate-only batches must not add history; stale state or pre-commit failure must not save part of a batch. Preserve original link paths/metadata and distinguish private-desktop handler tests from physical OLE verification.
- Read HISTORY.md before organizer UI changes. Do not restore the legacy frmClient/frmGroup bridge or a required group setup workflow. Keep the primary toolbar minimal, rename inline, and icon/pin actions optional in the context menu. Rename must preserve identity and automatic artwork policy. The user explicitly reprioritized implicit dragging over adjacent hardening work.
- The accepted product workflow is organizer-mediated drag-to-group: item onto item creates a group, onto a group adds, dragging out removes, one-member groups dissolve, and between-item drops reorder. Names/icons are automatic; customization is optional; generated links require no file hunting. Retain the popup launcher and provide undo. Read the updated roadmap before UI work.
- GroupPublishing offers manual shortcut guidance plus the milestone 4c native pin preview. Read HISTORY.md before changing it. Verify a projection against committed data before offering it; reject stale/hidden groups and do not infer pin success from Explorer startup. Read HISTORY.md: newer Windows releases remove the current-app LAF restriction, but per-group Start-menu identity and pin behavior still need proof. Do not invoke pin APIs in automated tests on the active desktop.
- Read HISTORY.md before native publishing work. NativePinProbe.cs is a separate read-only capability probe; with the application present it also tests the production adapter's queries. Its isolated eligibility=false result is contextual; its seven checks do not prove Start-menu resolution or actual group pinning. Keep all RequestPin APIs out of automated desktop probes.
- Native pin requests belong only to the dedicated --pin-group process with the persisted group AppUserModelID, following an explicit button action. Never request a group pin from the organizer's identity. Keep native request calls behind IPinClient in tests, use disposable Programs folders, retain the foreground/runtime gates, and keep cancellation nonmodal. Start-menu registration is an owned projection, not an organizer commit; unrelated entries must never be overwritten.
- Actual Windows taskbar drag interception and per-group pin automation remain feasibility questions. Do not conflate organizer changes with changes to real Explorer pins or import invasive code from the unrelated Taskbar Organizer checkout.
- Use IconService for shortcut artwork, return owned images and dispose them with their controls. Treat caches as optional; preserve display when cache writes fail. Read HISTORY.md for package artwork and invalidation limits. Keep network lookup off the manager constructor and cancel it on disposal.
- Preserve LaunchService as the shared popup launch boundary; read HISTORY.md for supported kinds, working-directory precedence and compatibility limits. Do not reintroduce direct click/key process launching or concatenate a target and user arguments into a shell command. Actual packaged/URI/folder activation still needs isolated interactive verification.
- Read HISTORY.md before popup geometry changes. Use Screen.FromPoint and that same monitor's Bounds/WorkingArea with PopupPlacement; never index a filtered taskbar list by monitor order or substitute primary-screen dimensions. Re-place expanded errors without activating the window. Synthetic geometry tests do not establish live auto-hide or mixed-DPI compatibility.
- Do not approve a compatibility patch based only on its title; review its actual diff and test the applicable behavior.

- Read HISTORY.md before pinned discovery changes. Pass the source explicitly; tests must never import the real profile. Preserve PinnedSources receipts through commits and undo/redo. Source files are read-only; snapshots are owned by LocalAppData. Import and receipts must publish atomically. Do not equate alphabetical discovery with Windows taskbar order or organizer reorder with Explorer pin movement.

- Read HISTORY.md before discovery changes. Live running-window discovery is explicitly opted in by production startup; injected tests must not enumerate the user's desktop. Never capture window titles or copy arbitrary process command lines. Preserve packaged identity and pin metadata precedence; discovery is not an exact taskbar mirror.

- Read HISTORY.md for Release bitness and package-root artwork regressions. Keep Prefer32Bit=false in Release as well as Debug. Loading the assembly into an x64 harness alone does not verify executable startup architecture.

## Build and verification

- Use the `remote-build-worker` skill for sustained compilation on `dockerbox`, bounded at two MSBuild workers by default. Keep remote work under `C:\Sandbox\Codex` and preserve incremental outputs/packages.
- Use full Visual Studio MSBuild for this legacy project and its COM references. Do not assume `dotnet build` is interchangeable.
- Read `VERIFICATION.md` for exact build commands and artifacts. Keep build outputs and disposable fixtures in ignored `out/` locally.
- The verification harness intentionally reproduces known defects: `PASS KNOWN KI-xx` means reproduction succeeded, not that the bug is fixed. Update such assertions to expect repaired behavior when implementing a fix.
- Test persistence failures, mixed valid/corrupt groups, missing icons, exact target arguments, and legacy migration where affected. Keep private-desktop smoke tests distinct from interactive Windows taskbar compatibility tests.
- Do not claim daily-use readiness from compilation or a private-desktop smoke pass alone.

## Editing and reporting

Use CodexLock and `apply_patch` for shared-file edits. Do not overwrite another task's uncommitted work. Do not push or publish changes unless requested. Update issue status with source and test evidence; retain unresolved limitations in the final report.
