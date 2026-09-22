# Icon recovery, editor failures and responsive startup

Milestone 3b implemented and verified 2026-09-21. This is the next portion of the hardening fork; the organizer model exists, but the drag-to-group surface is still pending.

## Changes

- IconService supplies independent bitmaps for executable, directory, ordinary link, URI and packaged-identity paths. Invalid/missing targets, failed package lookups and malformed links return a placeholder with an Icons log entry. Packaged identities no longer go through the filename-based .lnk path.
- Shell-link icon paths and resource indices are read through the shared COM reader. A failed custom resource falls back to the link target. Extracted native icon handles are destroyed in finally paths, including folder icons. [Microsoft ExtractIconEx ownership requirements](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-extracticonexw).
- Corrupt PNG caches fall through to extraction and can be replaced. Cache-directory and publication failures leave the extracted image usable. Writes stage and flush a temporary PNG, then move/replace it; failed temporary writes are cleaned up. Placeholder failures are not persisted as successful cache entries.
- Cache keys include member ID, icon-policy version, target kind/path and direct-file size/write timestamp. This also bypasses old wrongly generated icon caches. Same-basename entries remain distinct.
- Popup, manager previews and editor use the shared extraction boundary. Dynamic icon bitmaps are disposed with their owning controls; reorder/delete/reload dispose removed controls. Group image selection detaches from the source file, retains the previous image on failure, and reports an inline error.
- File and shell-item imports validate launch plans without launching. A bad item reports an editor error; later file items can still be added. Empty image drops and invalid reorder indices are handled. Explicit invalid edited working directories are rejected on save rather than silently rewritten.
- Saturated hover colors clamp each channel to 0–255. Group and legacy validation require an opaque valid background before constructing controls; malformed color-parser exceptions become validation failures. Saturated legacy colors can now pass migration validation.
- New immutable group generations contain 16, 32, 48, 64, 128 and 256px icon frames. Existing published generations are untouched.
- The manager starts release lookup after Shown, with a five-second deadline, cancellation on disposal, bounded response size and validated version tags. It no longer blocks construction with Task.Run(...).Result. Late completion cannot update/reopen a disposed window. Version failure appears as Unavailable.
- The release hyperlink and Explorer selection action dispatch through LaunchService and use manager status on failure. A source search finds no remaining MessageBox calls and only LaunchService calls Process.Start.

State schemas, organizer authority, stable identities and atomic group commits are unchanged. The current manager remains active; normal startup still does not activate the organizer store.

## Verification

Final Release build on dockerbox / HostPC with VS 2022 MSBuild 17.14, /m:2: **0 warnings, 0 errors**. Reused the existing NuGet packages and incremental worker directories; no toolchain installation. Worker inspection found 24 logical CPUs and approximately 18.5 GiB free RAM.

| Private-desktop suite | Passed |
| --- | ---: |
| UiProbe | 45 |
| LaunchProbe | 47 |
| OrganizerProbe | 69 |
| PersistenceProbe | 45 |
| StorageProbe | 29 |
| Total | **235** |

All suites exited 0 against the same final executable. New checks cover corrupt/read-only/unavailable caches, successful native extraction versus placeholders, detached image ownership, target timestamp invalidation, saturated colors and rejected invalid saves, legacy validation, six icon frames, invalid-then-valid editor imports, reorder/delete disposal, preserved editor images, empty drops, malformed release JSON, delayed lookup, timeout, cancellation, late completion and native modal-window absence.

Cached icon pixels were compared after accounting for alpha; maximum observed channel difference was 1, within the rounding tolerance. Test development exposed and repaired color-parser exception wrapping and repeated-disposal cancellation cleanup. Packaged routing/failure and release lookup timing use internal delegate seams with no user-facing activation switches.

The earlier regressions again cover process-interrupted commits, undo/redo, conflicting writers, migration and protected installation folders. Only LaunchProbe's disposable helper was launched (directly and through a link). No browser, Explorer or packaged app was activated; no real profile or taskbar pins changed, and no input desktop was switched.

Logs: verification/Milestone3bResults.txt. Final fixtures: out/Milestone3b-Verified-Ui, -Launch, -Organizer, -Persistence and -Storage.

## Artifacts and reproduction

- Remote source: C:\Sandbox\Codex\Workspaces\taskbar-groups.
- Remote intermediates: C:\Sandbox\Codex\Builds\taskbar-groups\Milestone3b.
- Remote output: C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone3b.
- Remote log: C:\Sandbox\Codex\Logs\taskbar-groups\Milestone3b.log; local out/Milestone3b.log.
- Clean package: out/TaskbarGroups-Milestone3b.zip, excluding probe executables and fixtures.
- EXE SHA256: B0E672B0455BEF886E7FE6AC7777F13686EB98937163A2C7186B0DB7BAF19562.

Use the MSBuild command in VERIFICATION.md with these output/intermediate paths. Compile UiProbe.cs like StorageProbe.cs in MILESTONE1.md, then run each probe in a fresh disposable directory with a hidden parent and bounded wait. The probes create and verify their private desktops.

## Limits and next step

The next product step is wiring the tested organizer model to the requested drag/drop surface, including undo and clear grouping/reordering zones. No drag UI or automatic taskbar pin publishing is claimed here.

Remaining hardening includes a dedicated whole-codebase analyzer/resource audit, cache/generation retention, selective recovery and real Windows compatibility checks. Cache invalidation does not yet track transitive .lnk target/custom-resource changes or package asset updates; old cache files remain retained. Package artwork still uses legacy manifest/logo-selection logic, so some installed apps can show a placeholder or generic artwork. Actual packaged artwork across an app catalog, native packaged/URI/folder activation, auxiliary Explorer/release-link actions, fractional-DPI quality and monitor placement remain unverified. Image/shell extraction remains synchronous and may be slow for network targets or shell extensions. A valid large image can still consume significant memory; no full image-decoding resource budget is implemented.

The source search and targeted failure tests are not proof that every external API failure is isolated. System file-picker dialogs remain user-invoked UI, and target applications can display their own UI. Nothing has been committed or pushed.
