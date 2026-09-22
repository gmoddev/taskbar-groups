# Implementation history

The September 2026 hardening and organizer work is consolidated here. Detailed former milestone reports, baseline probes and historical logs remain available in Git history at commit `cebee5d`. Current test evidence is in [VERIFICATION.md](VERIFICATION.md) and [verification/Results.txt](verification/Results.txt).

## Storage and identity

Milestones 1 and 2a moved writable state to `%LOCALAPPDATA%\TaskbarGroups`, removed automatic elevation, made legacy imports read-only and introduced stable group/member GUIDs. Display names no longer define storage paths. Legacy aliases and AppUserModelIDs remain stable through renames.

Group generations contain configuration, artwork and launcher links. Each save stages a new immutable generation, validates and flushes it, then atomically replaces a pointer. Previous snapshots provide recovery; deletes create tombstones rather than erasing history. Generated links are repairable projections. Failed pre-commit saves preserve the previous state.

Milestone 2b made the organizer snapshot the authority after activation. Membership, ordering, group generations and undo/redo publish together under one writer lock. Revision tokens reject stale editors. Undo/redo retain up to 50 entries and always issue a fresh revision. Popup-only startup does not activate the organizer. Existing healthy groups import once; later legacy merges need an explicit future workflow.

Two-member groups dissolve when a member leaves. The old launcher identity remains a redirect to a one-item popup. Empty groups become tombstones. Resources remain retained; pruning and recovery UI are unfinished.

## Launching and artwork

Milestones 3a/3b introduced a shared LaunchService for executable, link, directory, URI and packaged-app launch plans. Validation never dispatches a target. Links retain arguments and working-directory precedence, with bounded chain depth, cycle rejection and deterministic COM cleanup. Errors are nonmodal and isolated per item.

IconService owns returned images, handles unavailable icons and treats caches as optional. Icon invalidation, editor boundaries, multi-resolution artwork and asynchronous release lookup replaced fragile legacy behavior. Broader resource analysis and actual packaged/URI/folder activation remain unverified.

Milestone 4h corrected two regressions: Release now explicitly sets Prefer32Bit=false, preventing Brave's shortcut from resolving under the x86 Program Files directory on x64 Windows. Package artwork now supports root-level logos, app-specific assets, qualified variants, corrupt-candidate fallback and path containment. Codex's installed artwork and Brave import were checked directly without touching the real profile.

## Organizer workflow

Milestones 4a, 4d and 4e made frmOrganizer the default no-argument surface. Center drops group, edge drops reorder, member drag-out removes, and one-member groups dissolve. Names/icons/launcher links are automatic. External file drops advertise Copy and import accepted items in one atomic undoable operation; invalid inputs are skipped and duplicate-only batches add no history.

Internal drags carry origin and revision. Self/stale drops are rejected. The toolbar is Add apps/Undo/Redo, with inline rename and optional context actions. The old editor classes remain for regression coverage but are no longer exposed. Deferred context actions avoid disposing controls inside their own handlers.

Milestones 4f/4g added a compact horizontal strip, copied pinned shortcuts and visible running-app discovery. Pin copies preserve metadata and survive source removal. Receipts publish with imports and survive undo so a restart does not resurrect undone entries. F5 rescans.

Live discovery is production-only; tests inject sources. It reads visibility/style/cloaking, package identity and relaunch metadata, excludes tool/owned/cloaked/shell windows and the organizer, and resolves Steam's browser host to its launcher. It does not capture window titles or arbitrary process command lines. Pins take precedence; running candidates deduplicate by resolved target. A disposable live scan found nine apps with no skipped entries.

## Pinning and Windows compatibility

Milestones 4b/4c provide manual guidance and a native pin preview. New groups automatically reveal inline pin options. Native requests belong to a dedicated process with the group's AppUserModelID, an owned per-user Start-menu registration, an explicit button action and foreground/runtime checks. Registration validates revision and ownership, refuses unrelated entries and writes atomically.

The native adapter uses bounded queries/requests and cancellation; tests fake requests and only query real capabilities read-only. On Windows 26200.9445, capability queries worked and no limited-access token was required, but isolated non-foreground eligibility was false. This is not proof of actual per-group pin creation.

Windows confirmation is still required. Organizer changes do not move, remove or replace Explorer pins. Closed packaged/system pins without links can be missing; discovered ordering is not taskbar order. No Explorer injection, global hook, Taskband writes, service, driver or policy changes were added.

Milestone 5a uses the clicked monitor's bounds/working area for popup placement, including negative coordinates and error resizing. Live auto-hide and mixed-DPI behavior remain unverified.

## Verification and publication

The final implementation passed 393 private-desktop checks: Surface 128, UI 68, Launch 47, Organizer 69, Persistence 45, Storage 29, NativePin 7. Release compilation produced zero warnings/errors. Tests cover interruption, rollback, recovery, conflicts, grouping, history, imports, launch metadata, icon fallback and geometry. Only a disposable launch helper was executed; real pins and profiles were not modified.

Source was published on master as `cebee5d`, with the [organizer preview release](https://github.com/gmoddev/taskbar-groups/releases/tag/organizer-preview-2026-09-22). Its executable SHA256 is `7208258231EEFEEE37D25A6AEBE16837FB6026ABA0B24300690724E638ADB6B8`. Cleanup changes documentation/artifact organization only; it does not change application behavior.
