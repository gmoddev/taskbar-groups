# Brave discovery and package artwork fixes

Fixed 2026-09-22. Release|AnyCPU omitted Prefer32Bit=false, although Debug specified it. The shipped Release therefore preferred x86 on this x64 machine. Reading Brave's environment-based shortcut from a 32-bit process produced a nonexistent Program Files (x86) path. Earlier x64 harnesses missed this executable-startup difference. Release now explicitly disables 32-bit preference. A regression assertion checks both preferred and required 32-bit PE flags.

The package icon reader also assumed the manifest logo contained a directory separator. Codex uses a package-root logo, causing a negative-length Substring and a red error icon. The reader now selects artwork for the requested application, falls back to the package logo, supports root-level files and matching qualified filenames, and isolates corrupt candidates. Paths outside the package root are rejected. Import warnings now identify the skipped shortcut by filename.

Read-only reproduction confirmed Brave's target resolved incorrectly under 32-bit PowerShell. Against the corrected assembly in a disposable profile, Brave imported with zero skipped shortcuts. Codex's real installed package artwork rendered successfully as a 64x64 image and was visually inspected. No real profile, pin or target application was changed during these checks.

Release on dockerbox / HostPC, VS MSBuild 17.14, /m:2: zero warnings/errors. **393 private-desktop checks pass**, all suites exit 0: Surface 128, UI 68, Launch 47, Organizer 69, Persistence 45, Storage 29, NativePin 7. New tests cover Release PE flags, root logos, application-specific qualified assets, corrupt-exact fallback, traversal and missing logos. Native pin requests remain faked. Windows taskbar synchronization and full Windows/DPI/package compatibility remain unverified.

Runtime: out/IconFixBuild. Package: out/TaskbarGroups-Milestone4h.zip. Results: verification/Milestone4hResults.txt. Fixtures: out/IconFix-{Surface,Ui,Launch,Organizer,Persistence,Storage,NativePin}. Log: out/Milestone4h.log. Worker output/intermediates use Milestone4h under C:\Sandbox\Codex. The running out/Latest process was left untouched; close the old app and use the new build.

EXE SHA256: 7208258231EEFEEE37D25A6AEBE16837FB6026ABA0B24300690724E638ADB6B8. The user authorized publishing the accumulated fork implementation and this fix to gmoddev/taskbar-groups.
