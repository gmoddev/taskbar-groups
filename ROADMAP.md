# Taskbar Groups roadmap: drag-to-group workflow

Latest: [Milestone 4h](MILESTONE4H.md) fixes Release preferring x86 (Brave resolved under the wrong Program Files directory) and Codex's package-root logo. 393 checks pass, zero build warnings/errors; real Brave import and Codex artwork were checked without modifying the user profile. Runtime: out/IconFixBuild. The user authorized publishing the accumulated work to their fork. Taskbar synchronization remains separate and incomplete.


Latest correction: [Milestone 4g](MILESTONE4G.md) merges pinned shortcuts and visible running apps, including packaged identities and Steam's real launcher; F5 rescans. 387 isolated checks pass. A disposable live scan found nine unique apps, zero skipped. Runtime is out/DiscoveryBuild, convenience copy out/Latest. Closed packaged/system pins without links remain a discovery gap; ordering is still independent of Explorer. Preserve source receipts and existing groups when extending discovery.


Latest milestone: [4f](MILESTONE4F.md), 2026-09-22. Automatic pinned-shortcut discovery and durable copies, a compact horizontal strip, and inline pin options after grouping are implemented. **372 isolated checks pass**, Release has zero warnings/errors. Runtime: out/Milestone4f-Build; package: out/TaskbarGroups-Milestone4f.zip. Initial imported order is alphabetical, discovery may omit packaged/system pins, and organizer gestures do not reorder/remove real Windows pins. Native pinning still requires Windows confirmation and actual per-group pin creation remains unverified. Next priorities: validate that native flow interactively, improve discovery/order fidelity without hooks, and add explicit reimport/refresh controls. Preserve import receipts across undo and all commits; do not downgrade a receipt-bearing profile.


Updated 2026-09-21 from baseline `edbd4d9116f10597e9c16934df783be9728a98ab`. Milestones 0 and 1 and milestone 2's persistence/model foundations are implemented; disk retention and actual taskbar publishing remain pending. The user has selected direct manipulation as the intended grouping workflow. The core organizer UI is implemented in milestone 4a; native taskbar publishing and .NET 10 migration remain pending; a guided Windows pin flow is implemented.

Keep the existing design: ordinary pinned shell link -> `TaskbarGroups.exe <identity>` -> a small popup -> selected target. No Explorer injection, taskbar hooks, process-memory changes, service, driver, or DLL patching.

## Product destination

Priority reaffirmed 2026-09-22: direct dragging is the core product, not a secondary feature behind configuration or publishing. [Milestone 4e](MILESTONE4E.md) removes the legacy editor bridge, trims the toolbar to Add apps/Undo/Redo, collapses unused member space and makes rename/icon/pin actions optional. 354 checks pass. Prioritize physical workflow verification and compact optional item details; do not expand the old editor-first UI.

Grouping should be an immediate drag operation in one organizer surface, with the existing popup retained for launching. Conceptually combine Taskbar Organizer's icon-management UI with Taskbar Groups' launcher; this does **not** authorize importing the sibling project's global mouse hook or Explorer-memory logic.

```text
[Chrome] [Firefox] [VS Code] [Discord]
          drag Firefox onto Chrome
                     ↓
[Group 1: Chrome + Firefox] [VS Code] [Discord]
```

| Gesture | Required result |
| --- | --- |
| Drop A onto B | Create a group containing B and A, with a stable ID, automatic name/icon, saved membership, and generated launcher link |
| Drop C onto an existing group | Add C without opening an editor |
| Drag C out of a group into the organizer | Remove C from membership and restore it as a standalone item at the drop position |
| Group reaches one member | Dissolve it in the organizer and restore its remaining member at the former group position |
| Group reaches zero members | Remove the empty group without deleting target files |
| Drop between items | Reorder; do not accidentally group |
| Rename or choose another icon | Optional customization after grouping |

There must be no required category dialog, name entry, icon picker, shortcut-file browsing, or manual file generation. Generated names can start with `Group 1`; composite icons come from members. Every operation supports undo and persists automatically.

The first implementation operates in **our organizer window**. Updating membership of an already pinned group should not require repinning. Reproducing the before/after arrangement on the real Windows taskbar is a separate integration concern: do not silently remove existing app pins or pretend the organizer's order is Explorer's order.

## 0. Establish a baseline — complete

- [x] Clone the corrected repository and record the commit.
- [x] Restore dependencies and compile the unchanged Release application.
- [x] Run isolated manager/popup smoke checks and focused fault probes.
- [x] Map all eight supplied priorities to source evidence and limitations.
- [x] Review relevant upstream compatibility diffs and record adoption caveats.
- [x] Document build, artifacts, architecture, agent rules, and outstanding verification.

## 1. Per-user storage and unelevated startup — implemented

Addresses KI-01, KI-02, and part of KI-08.

- [x] Separate executable location from `%LOCALAPPDATA%\TaskbarGroups` with one path provider for configuration, links, caches, profiles, migration records, and logs.
- [x] Remove the write-test document, `runas`, and self-kill; embed `asInvoker`; report unavailable startup storage through logs and exit code 1.
- [x] Copy and validate legacy groups in staging, retain originals, preserve existing user data, and record imports so retries do not resurrect deleted groups.
- [x] Remove working-directory dependence from configuration/images/links; use the actual EXE path in generated links.

Verification: private-desktop checks exercise denied installation writes, unrelated working directories, migration retry/conflict/recovery, valid and corrupt legacy files, shortcut readback, manager/popup startup, and unavailable user storage. See `MILESTONE1.md`. The group-save and cache-path defects are addressed by milestone 2a below.

## 2. Recoverable persistence and stable identity — model and commits implemented

Addresses KI-05 and KI-06; depends on milestone 1's storage root. See [MILESTONE2A.md](MILESTONE2A.md) for group saves and [MILESTONE2B.md](MILESTONE2B.md) for organizer authority, undo, and 143 passing checks. The default organizer surface now activates the store (milestone 4a); popup-only startup does not.

- [x] Add a versioned group schema with stable group/member IDs and separate display names. Retain original legacy directory keys as permanent CLI aliases and preserve their AppUserModelIDs.
- [x] Stage XML, images, and the launcher in immutable generations; flush, validate, and atomically publish the group pointer. Keep previous generations and retry generated-link publication from committed data.
- [x] Remove delete-then-create editing; use editor copies, writer locking, revision conflict detection, recovery, and deletion tombstones.
- [x] Validate storage identities and containment. Key disposable caches by member identity and target path.
- [x] Add an ordered organizer model containing standalone items and groups, persisted positions, and operation boundaries independent of UI controls.
- [x] Extend the commit boundary to one authoritative organizer snapshot for atomic multi-group moves, dissolution, reorder, and durable undo/redo. Existing editors join that authority after activation.
- [ ] Add bounded retention of unreferenced generations and abandoned staging, plus a restoration UI, without removing resources still used by old pins.

Completed acceptance: process exits at six publication stages leave a complete old/new group; stale edits/deletes are rejected; rename retains legacy CLI resolution and AppUserModelID; special display names are not paths; corrupt current state can recover a previous generation. Actual Explorer pin behavior remains a separate interactive test.

Milestone 2b acceptance passed: moving an item between groups, creating/dissolving groups, and changing organizer order commit or roll back as one operation; undo/redo restore identities/order across process restart. History stacks are capped at 50. Disk cleanup and selective salvage remain pending.

## 3. Launch contract, cache repair, and failure isolation — core paths implemented

Addresses KI-03, KI-04, KI-08 and resource ownership findings. [Milestone 3b](MILESTONE3B.md) implements icon/cache recovery, editor image/import boundaries, color validation and asynchronous startup checks. The final build passes 235 private-desktop checks.

- [x] Implement the shared launch contract for executables, ordinary links, packaged identities, folders and registered URIs, with explicit shell execution, working-directory precedence and separate arguments. See [MILESTONE3A.md](MILESTONE3A.md) for restrictions and verification limits. Real packaged/URI/folder activation remains to be tested.
- [x] Share the same launch/error boundary across mouse clicks, number keys and open-all. Invalid entries return a quiet result and later valid items continue; popup errors appear inline and in logs. Milestone 3a passes 47 launch checks plus all 143 previous regression checks.
- [x] Repair corrupt caches, keep extraction usable when cache storage fails, share icon-kind handling and supply 16–256px group icon frames. Milestone 3b adds direct-file timestamp invalidation and targeted image/native disposal.
- [ ] Complete package artwork coverage, transitive asset invalidation, bounded cache/generation retention and the full native-resource audit.
- [x] Validate group XML/layout/opacity/color before display; clamp hover components and use placeholders at the shared icon boundary. Bad imports do not stop later valid file items. Selective salvage of bad persisted members remains a separate recovery feature.
- [x] Remove editor/icon MessageBox failures and route auxiliary launches through LaunchService. Release lookup starts after Shown with a five-second limit, cancellation and bounded/validated response data. Native auxiliary launches still need isolated interactive verification.
- Add compiler/analyzer coverage for the entire handwritten codebase and triage actionable diagnostics. The baseline build used compiler warnings; a dedicated analyzer sweep remains planned.

Acceptance: mixed healthy/corrupt groups load; a bad icon or target never opens a modal error; cache regeneration works with the editor closed; two identically named targets retain distinct icons; executable/.lnk/UWP/folder/URI tests cover spaces, Unicode, arguments, missing paths, and invalid working directories.

## 4. Organizer-mediated drag/drop — first surface implemented

[Milestone 4a](MILESTONE4A.md) supplies the default WinForms organizer, tested through 31 surface checks plus 235 regressions. Center/edge drops, member strip, dissolution, keyboard grouping/moves, durable undo/redo and optional group settings are wired to the existing store. Physical OLE mouse interaction and the full compatibility matrix remain unverified.

- [x] Wire OrganizerStore operations to a surface that displays groups and standalone entries; activate on opening it.
- [x] Accept external file/shortcut drops onto an app, existing group or member strip; edge drops insert standalone items. Batch import is one commit/undo entry, with duplicate filtering, source preservation and rollback. [Milestone 4d](MILESTONE4D.md): 326 checks pass; physical OLE interaction remains unverified.
- [ ] Add deliberate merging of newly discovered legacy directories after activation.
- Replace the required editor-first flow with a unified strip/grid of standalone applications and groups. Support adding launchable items from ordinary file/shortcut drops and an app picker; resolve running applications only through supported APIs. Do not assume a running window is already a durable launch specification.
- Implement distinct drop zones: center hover previews grouping, boundaries preview reordering. Use a drag threshold and clear target highlight; Escape cancels with no changes.
- Implement all gestures in the product table. Dragging out changes membership only; it never removes a target file, uninstalls an app, closes a process, or unpins an unrelated Windows icon.
- Use default names and composite icons, write the model, and generate/update the `.lnk` automatically as one recoverable operation. Do not display success until persistence succeeds; failures roll back the gesture and appear in the organizer's status area.
- Reject self-drops and duplicate membership without modal errors. Defer nested groups until an explicit product decision; the initial release has one group level.
- Preserve order and launch metadata through moves and undo. When dissolving a group, preserve its old launcher identity as a redirect to the survivor until a supported pin transition is completed; do not strand existing pins.
- Keep rename/icon/argument customization optional in a compact context action or details panel. Add keyboard equivalents and accessible descriptions for drop actions.

Acceptance: A-on-B groups immediately, C-on-group adds, drag-out removes, one-member groups dissolve, boundary drops reorder, and undo restores each state after save/relaunch. No category wizard or file-search step is required. Invalid drops and storage failures leave the previous layout intact. Existing group pins keep working after membership edits and renames.

### Taskbar publishing and existing-group drops — guided flow implemented

- [x] Expose Pin group with inline instructions and a Show shortcut action that repairs/verifies the committed link and requests exact Explorer selection. [Milestone 4b](MILESTONE4B.md): 277 checks pass; the final Windows pin action remains manual and unverified.
- [x] Run the read-only native capability prototype on Windows 26200.9445: desktop support and pinned-state queries work; the documented LAF predicate requires no token. Four probe checks pass. Eligibility is false on the private non-foreground desktop; actual group identity resolution remains unverified. See [native pin feasibility](NATIVE_PIN_FEASIBILITY.md).
- [x] Implement the native current-app pin preview in a separate group-identity window with owned Start-menu registration, eligibility gates and inline outcomes. [Milestone 4c](MILESTONE4C.md): 306 checks pass; actual native requests were not exercised.
- [ ] Interactively validate the native per-group pin preview and Start-menu entry resolution. Current Microsoft guidance removes LAF restrictions on newer builds; do not treat restricted access as a universal blocker. Validate per-group identity resolution, policy, user cancellation and actual pin behavior.
- [ ] Manage registered Start-menu display metadata and lifecycle across rename, move, deletion and dissolution, preserving entries still used by existing pins.
- Treat pin approval as a Windows interaction, not permission to force registry/taskbar-database changes. Microsoft's [current pinning guidance](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/pin-to-taskbar) and [pin request API](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.taskbarmanager.requestpincurrentappasync) describe user confirmation; requesting a current-app pin does not prove arbitrary generated group links can be pinned automatically.
- [x] Support `.lnk`/application drops on organizer groups and member surfaces using normal WinForms drag/drop (milestone 4d).
- [ ] Extend supported drops to the popup and investigate whether an actual pinned shortcut can deliver a supported drop payload. Do not claim the taskbar route works before a proof of concept.
- Treat drop payloads as additions to a group, never as commands to execute during import. Preserve existing link arguments and validate target kinds.

Acceptance: the app creates/updates links automatically and users never browse the generated-files directory. Any unavoidable OS pin confirmation is explicit. Cancellation leaves the organizer group usable. Actual taskbar support is tested on Windows 11 rather than inferred from organizer success.

### True taskbar icon-on-icon grouping — feasibility only

Investigate whether supported Windows 11 mechanisms can expose the specific `pinned icon A dropped onto pinned icon B` gesture and support the resulting pin changes. An ordinary drag/drop handler in our window is not evidence of this capability. Record API constraints and a reproducible proof if one exists. If it requires Explorer injection, global taskbar hooks, or internal taskbar manipulation, report that incompatibility with the current architecture constraints and keep the organizer workflow; do not silently introduce invasive integration.

## 5. Windows compatibility, with selective upstream adoption

- [x] Replace filtered taskbar-list indexing with clicked/nearest-monitor working-area placement, including negative coordinates, off-screen anchors and expanded error labels. [Milestone 5a](MILESTONE5A.md): 349 checks pass.
- [ ] Verify physical mixed-DPI/display configurations, live auto-hide expansion and monitor changes while open; add oversized-popup scrolling. Synthetic placement tests are not the full compatibility matrix.

Addresses KI-07. The linked fork has no open PRs at the time checked; the useful candidates are in `tjackenpacken/taskbar-groups`. These are source assessments, not endorsements or results from running those branches.

| Upstream PR / reviewed head | Assessment | Action |
| --- | --- | --- |
| [#346](https://github.com/tjackenpacken/taskbar-groups/pull/346), `aac02edb4ac22adb4f9f3e1d5a49f60cdd1f60fb` | Absolute `CreateConfig` paths and explicit EXE target address real path defects; includes a wired application manifest. Still stores state beside the EXE and leaves other relative paths. A manifest does not prove full Windows 11 compatibility. | Adapt path logic to the new per-user provider; verify manifest behavior independently. |
| [#109](https://github.com/tjackenpacken/taskbar-groups/pull/109), `000278f8c28b1fcdd7f89dd5f7f57a1f7766fc58` | Maps taskbar rectangles by screen device name instead of inconsistent list indices. | Prefer this identity-based direction; verify placement on taskbar-less displays and negative-coordinate monitors. |
| [#340](https://github.com/tjackenpacken/taskbar-groups/pull/340), `fef49bac4319b870b59c7e69cadffce895630d4a` | Adds a rectangle for every screen, addressing the list-length mismatch. Also adds an unconditional Debug wait for a debugger. | Compare with #109; do not import the debugger wait. Test auto-hide/empty-rectangle placement. |
| [#255](https://github.com/tjackenpacken/taskbar-groups/pull/255), `a666fbe00bb96bb2a2f3a6bc25ecb5fe64bfeaec` | Addresses DPI, but `eDpi / 96` uses integer division and `Display(...)` runs before `mouseClick` is assigned in the constructor. Fractional scales and secondary monitors need correction. | Rework around per-monitor DPI; do not merge as-is. |
| [#354](https://github.com/tjackenpacken/taskbar-groups/pull/354), `87bcccd5cbf389513a62a68adeb9d57762714856` | Broad feature/identity/update changes; inspected launch diff adds `UseShellExecute = true`, already the Framework default. Project diff changes Release to x64. | Review individual fixes separately. Do not import another fork's update URLs/AppIDs or infer launch correctness from this one flag. Full PR assessment remains pending. |
| [#246](https://github.com/tjackenpacken/taskbar-groups/pull/246), `2a19c49829bc278f897df1973dcb655708d6438d` | Comment spelling change only. | Does not repair the cache loop despite touching Category.cs. |

Acceptance: manual Windows 10/11 matrix covers actual pin/unpin/relaunch, distinct AppUserModelIDs, Explorer restart, 100/125/150/200% and mixed DPI, multiple monitors, negative coordinates, auto-hide, and a taskbar on only one display. Run in a disposable interactive environment, not while the user is gaming. Do not add hooks to work around platform behavior.

## 6. .NET 10 while retaining WinForms

Addresses KI-09. .NET 10 is an active LTS release as of this review; Microsoft lists support through November 14, 2028. Use a supported current servicing patch. [Support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

- Migrate to an SDK-style Windows-targeted .NET 10 project with WinForms. Preserve the popup/link architecture and legacy data compatibility.
- Audit Windows API Code Pack, TxFileManager, COM wrappers, direct WinRT metadata, resource generation, and designer code. Replace or adapt dependencies based on required APIs, not package age alone.
- Move away from `Assembly.CodeBase` path assumptions and unnecessary Framework profile/update APIs. Explicitly test the changed shell-execution default.
- Establish x64 build/package behavior first, then decide whether other architectures are supported. Validate deployment on a clean VM, including runtime requirements and all native/COM dependencies.
- Keep the Framework baseline available during migration and compare the same persistence and launch tests on both builds.

Acceptance: .NET 10 builds and runs from a clean install; all earlier milestones' regression tests pass; ordinary links and AppUserModelIDs persist across upgrades; no elevation, modal error, injection, or background-service dependency is introduced. See [Microsoft's WinForms migration guidance](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/migration/).

## Release gate

Daily-use readiness for the requested workflow requires safe persistence and launch handling, milestone 4's organizer gestures, tested supported taskbar publishing, milestone 5 compatibility checks, and a clean deployment test of the chosen runtime. Milestone 1 alone does not repair the remaining data-loss and failure-handling issues. True taskbar-native drag interception is a separate feasibility track, not a promised dependency of the organizer release.
