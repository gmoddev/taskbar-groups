# Known issues

Current status after the September 2026 organizer preview. Implementation history is consolidated in [HISTORY.md](HISTORY.md).

| Area | Current limitation |
| --- | --- |
| Real taskbar integration | Organizer grouping/order does not move, replace or remove Explorer pins. No taskbar-native icon-on-icon grouping. |
| Native pin preview | Windows confirmation is required. Read-only capability queries and fake request tests pass; actual distinct per-group pin creation is unverified. |
| Discovery | Closed packaged/system pins without a discoverable link, inaccessible/custom-hosted apps and cloaked windows can be absent. Order is not Explorer order. |
| Import lifecycle | Receipts prevent resurrection after undo. Previously imported source paths are not refreshed when contents change; explicit reimport/reset is pending. |
| Running-app metadata | Executable fallback does not restore live document/session arguments. Multiple profiles for one executable can collapse into an existing launch target. |
| Shortcut dependencies | Copied pins survive source removal, but nested links and external artwork remain dependencies. |
| Start-menu lifecycle | Owned registration exists; comprehensive rename/relocation/deletion maintenance remains pending. |
| Retention/recovery | Old generations and failed staging resources remain. Pruning, recovery UI and selective salvage are unfinished. Avoid profile downgrade. |
| Compatibility | Physical OLE, mixed-DPI/live auto-hide and actual packaged/URI/folder activation need interactive verification. Oversized popups are clamped rather than redesigned to scroll. |
| Legacy data | Existing healthy groups import at activation; later legacy merging requires an explicit future action. |
| Maintenance | Broader analyzer/resource audit and .NET 10 migration remain planned. |

The original priorities for per-user storage, removal of automatic UAC, dead cache loop repair, stable IDs, transactional saves and core failure isolation are implemented. Broader launch/compatibility/resource coverage remains bounded as above.

The latest fixes remove Release's 32-bit preference (Brave discovery) and the package-root logo assumption (Codex artwork). 393 private-desktop checks pass; see [VERIFICATION.md](VERIFICATION.md). Historical defect reproductions and detailed reports remain in Git at `cebee5d`.
