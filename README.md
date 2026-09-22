
  <img src="https://raw.githubusercontent.com/tjackenpacken/taskbar-groups/master/main/Icon.ico" alt="Taskbar Groups" width="96" />
</p>

# Taskbar Groups — drag to group

Available Windows pinned shortcuts and visible running apps appear automatically in a compact strip on startup. Drag an app onto another app to make a group immediately. Names, icons and launcher links are generated automatically; every change saves without a group editor.

- Drop an app or shortcut from Explorer onto another app to create a group.
- Drop onto a group to add it. Select the group to see its members.
- Drag a member back into the top area to remove it. A group with one member dissolves automatically.
- Drop at an edge to reorder or insert. A multi-file import is one undoable operation.
- Right-click a group for optional rename, icon or pin actions. Rename stays inline.
- Use Undo and Redo for mistakes. Original shortcut/application files are preserved.

The toolbar contains only Add apps, Undo and Redo. The old manager/editor is no longer opened by the organizer. Existing saved settings remain readable.

These gestures work **inside the organizer window**. Dragging one icon onto another directly on the Windows taskbar is not implemented. The launcher popup is retained.

## Opening and pinning

Select an app and press Enter, or right-click it and choose Open. Right-click a group and choose Pin to taskbar for Windows pin options. Native pin requests remain a preview; actual Windows confirmation and distinct per-group pins are not yet verified. Creating a group reveals these pin options automatically; Windows confirmation is still required. Reordering here does not move real Windows taskbar pins. Imported shortcuts start alphabetically; some packaged/system pins may be absent.

Keyboard alternatives: Ctrl+G groups with another item, Alt+Left/Right reorders, Ctrl+Shift+M moves a member out, Ctrl+Z/Ctrl+Y undo/redo, and F5 rescans for newly opened apps and pinned shortcuts. In inline rename, Enter saves and Escape cancels.

## Current build

[Milestone 4h](MILESTONE4H.md) fixes Brave discovery and package artwork. Release compilation has zero warnings/errors; **393 private-desktop checks pass**.

[Download the preview build](https://github.com/gmoddev/taskbar-groups/releases/tag/organizer-preview-2026-09-22). Extract the whole ZIP, close the older app, and run TaskbarGroups.exe. Physical Explorer dragging, native pin confirmation and the mixed-DPI/live auto-hide matrix remain unverified.

Configuration, generated links, caches and logs live under LocalAppData/TaskbarGroups. An explicit native pin action also prepares a per-user Start-menu entry. Legacy executable-adjacent groups are imported without deleting their originals. The application does not automatically elevate.

Read [known issues](KNOWN_ISSUES.md), the [roadmap](ROADMAP.md) and [build instructions](VERIFICATION.md). Recent reports: [external batch drops](MILESTONE4D.md), [native pin preview](MILESTONE4C.md), [popup placement](MILESTONE5A.md).

This working fork uses .NET Framework 4.7.2 and WinForms. A .NET 10 port remains planned. It uses ordinary shell links, AppUserModelID and a popup; no Explorer injection, taskbar hooks, service or driver.

## Source and attribution

Based on [gmoddev/taskbar-groups](https://github.com/gmoddev/taskbar-groups) and the original [tjackenpacken/taskbar-groups](https://github.com/tjackenpacken/taskbar-groups). See [LICENSE](LICENSE). Upstream release binaries do not contain this working fork's changes.
