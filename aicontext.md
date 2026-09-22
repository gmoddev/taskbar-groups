# Taskbar Groups context

Latest: [Milestone 4h](MILESTONE4H.md) fixes Release preferring x86 (Brave resolved under the wrong Program Files directory) and Codex's package-root logo. 393 checks pass, zero build warnings/errors; real Brave import and Codex artwork were checked without modifying the user profile. Runtime: out/IconFixBuild. The user authorized publishing the accumulated work to their fork. Taskbar synchronization remains separate and incomplete.


Latest correction: [Milestone 4g](MILESTONE4G.md) merges pinned shortcuts and visible running apps, including packaged identities and Steam's real launcher; F5 rescans. 387 isolated checks pass. A disposable live scan found nine unique apps, zero skipped. Runtime is out/DiscoveryBuild, convenience copy out/Latest. Closed packaged/system pins without links remain a discovery gap; ordering is still independent of Explorer. Preserve source receipts and existing groups when extending discovery.


Latest milestone: [4f](MILESTONE4F.md), 2026-09-22. Automatic pinned-shortcut discovery and durable copies, a compact horizontal strip, and inline pin options after grouping are implemented. **372 isolated checks pass**, Release has zero warnings/errors. Runtime: out/Milestone4f-Build; package: out/TaskbarGroups-Milestone4f.zip. Initial imported order is alphabetical, discovery may omit packaged/system pins, and organizer gestures do not reorder/remove real Windows pins. Native pinning still requires Windows confirmation and actual per-group pin creation remains unverified. Next priorities: validate that native flow interactively, improve discovery/order fidelity without hooks, and add explicit reimport/refresh controls. Preserve import receipts across undo and all commits; do not downgrade a receipt-bearing profile.


Baseline commit `edbd4d9116f10597e9c16934df783be9728a98ab`; milestones 1, 2a, 2b, 3a, 3b, 4a, 4b, 4c, 4d, 5a and 4e implemented and verified in the working tree through 2026-09-22.

Repository: https://github.com/gmoddev/taskbar-groups, branch `master`.
Upstream: https://github.com/tjackenpacken/taskbar-groups.
The sibling `Taskbar-Organizer` checkout was an initial incorrect repository supplied by the user. Do not use its C++ code or findings for this project.

## Current task and result

Milestones 1, 2a, 2b, 3a, 3b, 4a, 4b, 4c, 4d, 5a and 4e are implemented in the working tree. Milestone 2b adds ordered organizer state, atomic cross-group operations, one-item dissolution with retained launcher identities, and durable undo/redo. See [MILESTONE2B.md](MILESTONE2B.md). Milestone 3a adds a shared launch contract and quiet popup failure handling; see [MILESTONE3A.md](MILESTONE3A.md). Milestone 3b adds icon/cache repair, editor failure boundaries and responsive release lookup; see [MILESTONE3B.md](MILESTONE3B.md). Milestone 4a implements the organizer surface and makes it the default manager; see [MILESTONE4A.md](MILESTONE4A.md). Milestone 4b adds guided group pinning and verified shortcut preparation; see [MILESTONE4B.md](MILESTONE4B.md). Milestone 4c implements the native group pin preview and Start-menu publication; see [MILESTONE4C.md](MILESTONE4C.md). Milestone 4d adds external file/shortcut drops onto apps, groups and member surfaces, plus one-transaction batch import/undo; see [MILESTONE4D.md](MILESTONE4D.md). Milestone 5a replaces taskbar-list indexing with monitor-local popup placement and repositions expanded launch errors; see [MILESTONE5A.md](MILESTONE5A.md).

The latest Release build has zero warnings/errors. Private-desktop verification passed 89 organizer-surface/publishing checks, 68 UI/icon/placement checks, 47 launch checks, 69 organizer checks, 45 persistence checks, 29 storage checks and 7 read-only native checks (354 total). Fixtures were disposable, without real profile activation, taskbar pin changes or input-desktop switching. Only a disposable helper was launched, directly and through a link; actual packaged/URI/folder activation remains unverified. Nothing has been committed or pushed.

Ordinary no-argument startup now opens `frmOrganizer`, which calls `OrganizerStore.Initialize` and displays groups plus standalone items. Popup-only startup does not activate the store. The old frmClient/frmGroup classes remain for regression/compatibility work but are no longer opened by the organizer. Milestone 4e removes the editor bridge: dragging is primary, the toolbar has only Add apps/Undo/Redo, rename is inline, and icon/pin actions are optional context commands. See [MILESTONE4E.md](MILESTONE4E.md). Once a profile is activated, GroupStore readers/editors resolve exclusively through the global snapshot. Never publish per-group pointers for that profile. Existing healthy groups import once; later out-of-band legacy data needs a deliberate merge action.

A separate [native pin capability investigation](NATIVE_PIN_FEASIBILITY.md) now passes four private-desktop probe checks on Windows 26200.9445. Desktop support exists and the documented LAF predicate requires no token; eligibility is false in the isolated non-foreground process. Actual group Start-menu resolution and pin creation remain unverified. Milestone 4c adds a production native adapter; its read-only queries pass, while request outcomes are tested only through fakes. The native OS confirmation is never invoked during automation.

External imports now commit accepted inputs as one operation. Duplicate launch specifications are skipped globally; use internal dragging to move already imported items. External member drops append, edge drops insert standalone items, and imports never dispatch targets. Physical OLE mouse interaction remains unverified.

User priority correction: finish the direct manipulation experience before spending further milestones on adjacent hardening. The organizer simplification is implemented; physical drag testing and compact optional item details remain useful next work.

Next product work: interactively validate the implemented native per-group pin preview, manage registered Start-menu entries, later legacy-data merging and richer app import/editing; a guided Windows pin action now prepares and selects the group shortcut. Actual Explorer/pin actions remain unverified; the core organizer gestures and durable undo are now visible. Milestone 3's core launch/cache/editor boundaries are implemented; the full analyzer/resource audit and package/transitive-cache coverage remain open. Disk retention, recovery UI and real taskbar integration tests also remain open.

## Architecture

This is C# WinForms targeting **.NET Framework 4.7.2**, not modern .NET yet. `TaskbarGroups.sln` contains `main/client.csproj`, producing `TaskbarGroups.exe`.

```text
TaskbarGroups.exe                -> frmOrganizer management surface
TaskbarGroups.exe <group-id or legacy-name> -> frmMain popup -> selected target
ordinary .lnk + AppUserModelID   -> distinct group identity on taskbar
```

New links use GUID arguments. Original legacy directory names remain aliases; migrated groups retain their original AppUserModelID suffix. Renaming changes display text only. The inspected application uses normal shell links, AppUserModelID, WinForms, icon APIs, and Windows package metadata. No Explorer injection, global taskbar hook, process-memory patching, service, driver, or system-DLL patching was found in the compiled source. Opening Explorer to select a shortcut is ordinary shell usage.

## Source map

| File | Responsibility |
| --- | --- |
| `main/client.cs` | Entry point, per-user initialization, quiet startup storage errors, AppUserModelID, manager versus popup |
| `main/Classes/MainPath.cs` | Explicit executable/user-data paths, path validation, profile directories and storage diagnostics |
| `main/Classes/LegacyMigration.cs` | Copy/validate/stage/publish legacy groups, preserve source/current user data, import receipts and retry |
| `main/Classes/Category.cs` | Versioned group DTO, persistence facade, disposable member icon caches |
| `main/Classes/GroupPublishing.cs` | Verify committed links, launch a dedicated pin process, publish owned per-user Start-menu entries and request exact Explorer selection |
| `main/Classes/NativePinClient.cs` | WinRT pin adapter, process identity/runtime/foreground checks, bounded async queries and requests |
| `main/Forms/frmGroupPin.cs` | User-triggered group pin preview, inline outcomes, cancellation and manual fallback |
| `main/Classes/GroupStore.cs` | Stable identity/legacy lookup, generation staging and pointer commits, validation, locking/conflicts, recovery, tombstones and link publication |
| `main/Classes/OrganizerModel.cs` | Ordered layout, ownership validation and pure grouping/move/dissolution/reorder transformations |
| `main/Classes/OrganizerStore.cs` | Single snapshot authority, staged group generations, durable undo/redo, editor integration, activation and recovery |
| `main/Classes/ProgramShortcut.cs` | Target, arguments, working directory, Windows-app flag, display name |
| `main/Classes/ShellLink.cs` | COM shell link/property-store read/write with deterministic release |
| `main/Classes/IconService.cs` | Shared icon extraction/fallback, link resource indices and owned native handles |
| `main/Classes/LaunchService.cs` | Explicit launch plans, validation, shared dispatch and quiet result/log boundary; read MILESTONE3A.md for restrictions |
| `main/Forms/frmOrganizer.cs` | Default organizer, activation, drag/drop center/edge previews, member strip, keyboard actions, undo/redo and optional editor bridge |
| `main/Forms/frmClient.cs` | Load groups, manager controls, bounded asynchronous release lookup after Shown |
| `main/Forms/frmGroup.cs` | Editor, shortcut resolution, validation, save/delete transactions |
| `main/Forms/frmMain.cs` | Popup, monitor selection, keyboard actions, target launch and error re-placement |
| `main/Classes/PopupPlacement.cs` | Pure working-area placement, reserved edges, negative/off-screen anchors and clamping |
| `main/User controls/ucShortcut.cs` | Icon load and click dispatch through the shared launch service |
| `main/Classes/handleWindowsApp.cs` | Package lookup, app manifest/icon handling |
| `main/Classes/ImageFunctions.cs`, `IconFactory.cs`, `handleFolder.cs` | Image and shell icon helpers |

## State and dependencies

State lives under `%LOCALAPPDATA%\TaskbarGroups`. An activated profile uses `Organizer/Current.xml` as its single authority, with immutable `Organizer/Versions/<revision>.xml` snapshots containing layout, canonical items, group-generation references and history stacks. Old per-group pointers are then ignored. Legacy roots retain `config/<original-name>`; new roots are GUIDs. `Current.xml` selects immutable `Versions/<revision>/` XML/images/link; `Previous.xml` supports recovery. `Cache/` is disposable. Generated links use `Shortcuts/<group-id>.lnk`. Original legacy resources, tombstones and old generations remain retained; do not delete them casually or downgrade the executable. See the storage layout and retention limitations in MILESTONE2A.md.

Executable-adjacent `config/` is read-only migration input. Import receipts prevent reimport after deletion. `verification/Probe.cs` is historical baseline-only; use `StorageProbe.cs`, `PersistenceProbe.cs`, `OrganizerProbe.cs`, `LaunchProbe.cs`, `UiProbe.cs` and `SurfaceProbe.cs` in separate fresh private-desktop fixtures for current code.

NuGet packages: Windows API Code Pack Core/Shell 1.1.4 and TxFileManager 1.4.0. The project also uses WSH/Shell32 COM references and the checked-in `main/Windows.winmd`. Keep all runtime files from the build, including `Windows.winmd`; the EXE alone is not the deliverable.

## Next steps

Read [agent.md](agent.md), [KNOWN_ISSUES.md](KNOWN_ISSUES.md), [ROADMAP.md](ROADMAP.md), and [MILESTONE1.md](MILESTONE1.md). The ordered organizer foundation is implemented; core launch/cache isolation is implemented and the first visible direct-manipulation workflow is implemented. The .NET 10 port remains separate. Actual taskbar pin/drop capabilities need proof and must preserve the no-hooks architecture.
