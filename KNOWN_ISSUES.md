# Known issues

Latest: [Milestone 4h](MILESTONE4H.md) fixes Release preferring x86 (Brave resolved under the wrong Program Files directory) and Codex's package-root logo. 393 checks pass, zero build warnings/errors; real Brave import and Codex artwork were checked without modifying the user profile. Runtime: out/IconFixBuild. The user authorized publishing the accumulated work to their fork. Taskbar synchronization remains separate and incomplete.


Latest correction: [Milestone 4g](MILESTONE4G.md) merges pinned shortcuts and visible running apps, including packaged identities and Steam's real launcher; F5 rescans. 387 isolated checks pass. A disposable live scan found nine unique apps, zero skipped. Runtime is out/DiscoveryBuild, convenience copy out/Latest. Closed packaged/system pins without links remain a discovery gap; ordering is still independent of Explorer. Preserve source receipts and existing groups when extending discovery.


Latest milestone: [4f](MILESTONE4F.md), 2026-09-22. Automatic pinned-shortcut discovery and durable copies, a compact horizontal strip, and inline pin options after grouping are implemented. **372 isolated checks pass**, Release has zero warnings/errors. Runtime: out/Milestone4f-Build; package: out/TaskbarGroups-Milestone4f.zip. Initial imported order is alphabetical, discovery may omit packaged/system pins, and organizer gestures do not reorder/remove real Windows pins. Native pinning still requires Windows confirmation and actual per-group pin creation remains unverified. Next priorities: validate that native flow interactively, improve discovery/order fidelity without hooks, and add explicit reimport/refresh controls. Preserve import receipts across undo and all commits; do not downgrade a receipt-bearing profile.


Baseline: `edbd4d9116f10597e9c16934df783be9728a98ab`, reviewed 2026-09-21. Milestone 1 resolves KI-01/KI-02. Milestone 2a implements group-level transactional saves and stable identities (KI-05/KI-06), repairs the dead cache loop and cache-key defects, and isolates corrupt groups during manager loading. Milestone 2b adds authoritative organizer snapshots, atomic multi-group operations and durable undo/redo. Its model is tested; milestone 4a now provides the default organizer surface, activation, grouping/reorder gestures and undo/redo controls. Milestone 3a adds shared validated launch plans, quiet per-item popup failures and targeted shell-link COM ownership repairs. Milestone 3b adds shared icon/cache recovery, editor image/import boundaries, clamped colors, multi-resolution icons and responsive release lookup. Broader resource/compatibility/runtime work remains open. Detailed sections below preserve historical baseline evidence and line numbers; current changes and limits are in [MILESTONE1.md](MILESTONE1.md) and [MILESTONE2A.md](MILESTONE2A.md) and [MILESTONE2B.md](MILESTONE2B.md), plus [MILESTONE3A.md](MILESTONE3A.md) and [MILESTONE3B.md](MILESTONE3B.md), with [MILESTONE4A.md](MILESTONE4A.md) documenting the first organizer surface, and [MILESTONE4B.md](MILESTONE4B.md) for publishing limits.

`Reproduced` denotes an executable probe against the baseline assembly; `source-confirmed` denotes a traced code path; `unverified` denotes behavior still needing a suitable environment. Core drag-to-group behavior is implemented in the organizer window. Milestone 4b adds guided manual pinning with verified shortcut preparation and exact Explorer-selection dispatch. Native per-group pin APIs and taskbar-native drops remain planned; see ROADMAP.md and the interaction-testing limits in MILESTONE4A.md.

## Verification of the supplied priorities

| ID | Priority | Proposed issue | Verdict |
| --- | --- | --- | --- |
| KI-01 | P1 | Move state to LocalAppData | Resolved in milestone 1; storage, migration, and unrelated-working-directory tests passed |
| KI-02 | P1 | Remove automatic UAC elevation | Resolved in milestone 1; relaunch removed and compiled manifest verified asInvoker |
| KI-03 | P1 | Explicit process-launch behavior | Shared contract and popup failure boundary implemented in 3a; ordinary executable/link tests pass; actual packaged/URI/folder activation still unverified |
| KI-04 | P1 | Dead loop and related correctness bugs | Dead loop, editor dependence, extension mismatch and basename collisions repaired in 2a; cache repair, shared extraction and multi-resolution icons added in 3b; full resource audit pending |
| KI-05 | P1 | Transactional configuration writes | Group and organizer snapshot commits, recovery, conflicts and undo/redo implemented/tested in 2a/2b; disk retention/restoration UI pending |
| KI-06 | P1 | Stable group IDs | Group/member IDs and legacy aliases implemented in 2a; rename/CLI tests pass; actual Explorer pins unverified |
| KI-07 | P1 | Windows 11 compatibility review | Placement indexing/primary-screen geometry fixed in 5a; mixed-DPI/live auto-hide and full compatibility remain unverified |
| KI-08 | P1 | Isolate failures per item/group | Corrupt-group isolation, storage recovery and quiet missing-group startup implemented; quiet popup launch failures implemented in 3a; shared editor/import/icon boundaries added in 3b; selective salvage and broader API audit remain |
| KI-09 | P2 | .NET 10 WinForms migration | Baseline targets .NET Framework 4.7.2; migration is a proposal, not a current defect fix |

## KI-01: writable paths and working directory

`main/client.cs:32` derives `MainPath.path` from the EXE; lines 37-43 create JIT/config/shortcut state there. `Category.cs:65`, `:108`, `:122`, and `:155` use relative paths or `AppDomain.FriendlyName`. `frmGroup.cs:560` and `ucCategoryPanel.cs:29` also depend on the working directory.

Reproduced: `LoadIconImage()` fails after changing the working directory even with a valid image under the executable's configuration root. Consequences include writes to a different tree, missing icons, and incorrect generated shortcut target paths. An absolute-path fix alone does not make an installation in a protected directory writable.

## KI-02: automatic elevation and early unhandled writes

`main/client.cs:48-66` creates/deletes a test file and launches the same EXE with `Verb = "runas"` on any failure. It does not forward group arguments or terminate the original process after successful relaunch. Refusing elevation kills the original process. Directory creation occurs before the write-probe `try`, so a fresh read-only location can fail before even reaching that fallback.

Source-confirmed; deliberately not triggered on an interactive desktop. Remove the fallback rather than treating elevated operation as the installation solution.

## KI-03: launch routes and working-directory defaults

Current status: milestone 3a replaces popup launch paths with LaunchService and an inline failure label; clicks, keys and open-all share behavior. 47 checks pass, including real harmless executable/link dispatch and metadata tests for other kinds. See MILESTONE3A.md for explicit restrictions, unsupported link semantics and unverified native activation. The evidence below describes the historical baseline.

`frmMain.cs:338-358` passes stored arguments, target, and working directory to `Process.Start`, catches every exception, and displays a modal dialog. `ucShortcut.cs:37-40` launches Windows apps separately without that catch. `ProgramShortcut.cs:10` defaults the working directory to the executable FILE path; reproduced. The self-link special case at `ucShortcut.cs:43` requires a path to have `.lnk` extension and equal the application's `.exe` path, so it cannot normally match.

A benign executable launch with a quoted argument and an explicit valid working directory passed. This is not evidence that all `.lnk`, URI, folder, UWP, missing-target, or malformed-argument cases work. Stored arguments alone do not establish an exploitable injection vulnerability; no attacker-controlled entry path was demonstrated here.

`UseShellExecute` defaults to true on .NET Framework and false on modern .NET. Therefore, merely adding `UseShellExecute = true` is explicit documentation of the current ordinary launch behavior, not proof of a baseline Windows 11 fix. See [Microsoft's compatibility note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#change-in-default-value-of-useshellexecute).

## KI-04: cache loop and icon correctness

Current status: 2a removed the dead loop and cache-key defects. 3b adds corrupt-cache regeneration, independent extraction when cache storage fails, target timestamp invalidation, shared icon fallback, detached image ownership and six icon sizes. Package artwork, transitive invalidation and retention remain limited; see MILESTONE3B.md. The evidence below describes the baseline.

- **Reproduced:** `Category.cs:185` starts at `ShortcutList.Count` and loops while `i < 0`; a list count cannot be negative. `cacheIcons()` deletes the previous cache at line 174, then writes no entries. A populated group produced an empty replacement cache. Replacing only `<` with `>` would still index past the end; start at `Count - 1` with `i >= 0`, or use forward iteration.
- **Source-confirmed:** the loop also assumes a live `frmGroup` and positional UI controls (`:189`), even though other forms invoke cache rebuilding. Extract cache generation from editor controls before enabling the loop.
- **Source-confirmed:** folder icons are written as `_FolderObjTSKGRoup.png` (`:197`, `:237`) but read as `_FolderObjTSKGRoup.jpg` (`:224`).
- **Source-confirmed:** cache keys use basenames, allowing different targets with identical basenames to collide. Windows-app cache fallback does not consistently use the writer's key rule.
- **Source-confirmed:** `loadImageCache()` returns `Image.FromStream(ms)` while disposing `ms` (`:226-227`), and fallback extraction/saving inside the catch can itself throw. Clone decoded images into independent ownership and dispose temporary images/icons.
- **Source-confirmed:** `createMultiIcon()` stops before adding a 16px image; a nearest size of 16 produces no frames (`:140-151`).

Compiler success does not diagnose a semantically dead loop whose bound is a mutable list count.

## KI-05: old data can be lost before replacement succeeds

`Category.cs:79` opens the final XML file with `File.Create` (truncation) before serialization. A fault probe supplied an invalid surrogate in a shortcut argument: serialization failed and the previous valid XML was no longer intact.

On edit, `frmGroup.cs:534-540` uses TxFileManager to delete the old group directory and link, completes that transaction, and only later calls `CreateConfig` at line 559. Thus there IS a transaction, but its boundary excludes the new XML, image, icon cache, and shortcut. A later failure leaves committed deletion without a usable replacement. Images and links also use direct writes/moves.

A safe update needs staged resources, validated serialization, a same-volume atomic replacement strategy, and recoverable previous state. Atomic XML replacement alone is insufficient if dependent images and links are removed first. Concurrent editors need conflict handling as well.

## KI-06: display name is the persistence and taskbar identity

`frmGroup.cs:557` normalizes spaces to underscores. `Category.CreateConfig` embeds `Name` into directories, `.lnk` filenames, and AppUserModelID; `client.cs:73-75` uses the CLI name for identity and lookup. Rename can invalidate previously pinned links. The editor rejects many special characters (`frmGroup.cs:493`), so arbitrary punctuation through the normal UI should not be claimed as demonstrated. XML/CLI inputs still need validation and containment independently of the editor.

Introduce stable IDs with an explicit legacy-name-to-ID migration and preservation of old link arguments. Consider Windows reserved names, case-insensitive collisions, normalized-space collisions, and traversal in persisted/imported data.

## Direct interaction status

[Milestone 4e](MILESTONE4E.md) makes dragging primary and removes the organizer's legacy editor bridge. Rename is inline; icon/pin actions are optional context commands. The final build passes 354 checks. Legacy advanced editing controls are no longer exposed by the organizer; compact item-details editing remains pending. Actual taskbar icon-on-icon dragging is not implemented.

## Import workflow status

[Milestone 4d](MILESTONE4D.md) adds external file/shortcut drops onto organizer apps, groups and members, and atomic multi-file import with one undo entry. The final build passes 326 checks. Initial invalid inputs are skipped; failure before commit rolls back the entire accepted batch. Physical OLE dragging remains unverified. Already imported launch specifications are skipped globally; use internal dragging to move them. Popup and actual taskbar shortcut drop support remain pending.

## KI-07: placement, DPI, and compatibility claims

Current status: [milestone 5a](MILESTONE5A.md) removes taskbar-list indexing and primary-screen assumptions. The popup selects the clicked/nearest monitor, clamps within its working area and re-places expanded launch errors. Twenty synthetic geometry cases and three private-desktop integration checks pass; 349 checks pass overall. Oversized-content scrolling, display changes while open, mixed-DPI and live auto-hide behavior remain pending.

Baseline evidence: `frmMain.cs:100` indexes `taskbarList[i]` by screen index, but `FindDockedTaskBars()` adds entries only for screens with a docked area (`:204-246`). When a screen lacks a taskbar, indices can refer to the wrong screen or exceed the list. This is source-confirmed; the isolated smoke environment did not reproduce a mixed multi-monitor layout.

The baseline uses fixed pixel dimensions and had no explicit application manifest. The current build has an asInvoker manifest; full per-monitor DPI support remains pending. A manifest declaration alone cannot establish correct pinning, grouping, placement, icon scaling, or auto-hide behavior. The [upstream Windows 11 launch report #272](https://github.com/tjackenpacken/taskbar-groups/issues/272) is an external report, not a reproduction in this fork.

The local Windows build 26200.9445 private-desktop manager/popup smoke passed. Interactive pinning/grouping, monitor layouts, DPI, and Explorer restart behavior remain unverified. Candidate patch assessments are in `ROADMAP.md`. The separate read-only [native pin probe](NATIVE_PIN_FEASIBILITY.md) verifies desktop API support and a completed pinned-state query on build 26200.9445; eligibility is false in its non-foreground process. This does not verify group Start-menu identity or actual pin creation. [Milestone 4c](MILESTONE4C.md) adds the native request preview with fake-client UI coverage and production read-only checks (306 total). Native confirmation, two-group shell identity resolution, GUID shortcut display names and registered Start-menu lifecycle remain unverified or pending.

## KI-08: incomplete failure isolation

Current status: corrupt-group loading/storage recovery and popup per-item launch boundaries are implemented. Milestone 3b adds shared package/icon failure fallback, editor/import/image boundaries, saturated-color validation/clamping, asynchronous bounded release lookup and auxiliary LaunchService routing. No MessageBox calls remain in source. Full external-API failure coverage, selective salvage, actual package artwork and native auxiliary action verification remain open.

`Category.cs:45-54` deserializes without validation or an error boundary. `frmClient.Reload` catches only `IOException` (`:41`); malformed XML raises `InvalidOperationException`, reproduced directly. A bad group can therefore prevent normal manager construction. `frmMain.cs:46` reads the icon before checking that the group exists; missing group/icon throws, also reproduced.

Deserialized null lists, invalid opacity/color/width values, failed package lookups, and icon regeneration are not consistently isolated. Hover-color arithmetic at `frmMain.cs:63-66` can take a component outside 0-255 for saturated colors. Error branches use modal `MessageBox` calls. A missing ordinary target DOES render an error icon in the tested popup; failure tolerance exists in some paths, not all.

## KI-09 and additional maintenance issues

- **Runtime/dependencies:** `client.csproj:11` targets v4.7.2, uses legacy NuGet and COM references, and ships `Windows.winmd`. Any .NET 10 migration must address WinRT metadata/projections, COM, shell defaults, generated WinForms code, and packaging; it is not just a target-framework substitution.
- **Startup network wait:** `frmClient.cs:25` blocks construction on `Task.Run(...).Result`. `getVersionData()` creates another HttpClient and relies on its timeout. This historical delay is repaired in 3b: lookup begins after Shown, with a five-second limit, disposal cancellation and response validation.
- **Native/image ownership:** `ShellLink.cs:122` allocates an AppID string, but `PropVariantHelper` has no final clear/dispose path. `Category.CreateConfig` and `createMultiIcon` do not dispose all generated images. These are historical source-confirmed ownership gaps; milestone 3a adds clear/dispose/final release for the shared ShellLink reader/writer. Milestone 3b also disposes dynamic control images and extracted native icon handles. The complete ownership audit remains pending; sustained leak rates were not measured.
- **Configuration parity:** Debug explicitly disables Prefer32Bit; Release does not. Investigate before making architecture claims for shell/icon integrations.

The review and targeted probes are not an exhaustive security audit or a blanket compatibility certification.
