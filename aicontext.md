# Taskbar Groups context

Repository: gmoddev/taskbar-groups; upstream: tjackenpacken/taskbar-groups. Work only in this repository, not the sibling Taskbar-Organizer project.

The hardening and drag-to-group implementation was published as `cebee5d` on master. Read [agent.md](agent.md), [HISTORY.md](HISTORY.md), [KNOWN_ISSUES.md](KNOWN_ISSUES.md) and [VERIFICATION.md](VERIFICATION.md). Those replace the accumulated milestone reports.

## Current product

No-argument startup opens a compact organizer with pinned shortcuts and visible running apps. Drag onto an app to create a group, onto a group to add, between apps to reorder, and out of a group to remove. Groups dissolve at one member. Names/icons/links are automatic, rename is inline, and Undo/Redo persist. F5 rescans. The popup launcher remains.

The organizer does not mirror Windows pin order or silently pin/unpin apps. Native pinning is a user-confirmed preview and actual per-group pin creation is unverified. Discovery can miss closed packaged/system pins. Preserve the no-hooks architecture.

## Architecture and source map

.NET Framework 4.7.2 / WinForms, legacy main/client.csproj with COM references. Use full VS MSBuild, not dotnet build. Both Debug and Release disable 32-bit preference.

- client.cs: startup, storage initialization, manager/popup/pin process identities.
- MainPath / LegacyMigration: per-user paths, quiet failures, read-only legacy migration.
- GroupStore / OrganizerStore / OrganizerModel: immutable generations, atomic global snapshot, revision conflicts, group membership, redirects and history.
- LaunchService / ShellLink: explicit launch plans, shortcut metadata and COM ownership.
- IconService / handleWindowsApp: artwork, cache fallback and package logo resolution.
- PinnedApps / RunningApps: durable pin copies, receipts, read-only window discovery and deduplication.
- GroupPublishing / NativePinClient / frmGroupPin: owned shortcut/Start-menu projections and user-triggered native pin preview.
- frmOrganizer: minimal drag surface, imports, rename and undo/redo.
- frmMain / PopupPlacement: popup launching and monitor-local placement.
- verification/*Probe.cs: seven current private-desktop suites.

State lives under LocalAppData/TaskbarGroups. After activation, Organizer/Current.xml is the sole authority; it references immutable snapshots and group generations. Previous.xml provides fallback. Source receipts survive undo. Do not downgrade profiles, delete retained generations casually, or write per-group pointers after activation.

## Verified state

393 checks passed, all suites exited 0, Release compiled without warnings/errors. Brave import and Codex package artwork were directly checked. See verification/Results.txt for the retained evidence. Read-only capability queries are distinct from real pin confirmation.

Local out/ retains one current runtime in Release/, its published ZIP and Build.log. Superseded outputs are gathered in Archive/; deletion was blocked by automatic approval review. Historical reports/logs and archived build artifacts are not current instructions; tracked history can be retrieved from Git.
