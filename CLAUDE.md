# LaunchStage — notes for Claude Code

LaunchStage is a lightweight Windows workspace / automation app. One press of a **profile** (Stream, Code, ...)
closes or minimizes other apps, opens the profile's apps one at a time, and puts every window in an exact spot
across several monitors. Triggers: double-click a card, the tray menu, a Stream Deck button, or the command line.
Owner: Bones_84 (a streamer). Read README.md for the user-facing details of every feature.

## Build and run

- C# / .NET 10 (`net10.0-windows`), WPF. Solution in `src/`.
- `build.cmd` (repo root) is the only build step the owner uses: it runs `out\LaunchStage.exe --exit` if present,
  waits, then publishes **the CLI first and the app second** into `out\` (order matters: the CLI publish's clean
  step otherwise deletes the app's files).
- Run: `out\LaunchStage.exe` (window + tray) and `out\LaunchStageCli.exe` (command line).
- Stream Deck plugin: `build-streamdeck.cmd` (separate; `npm install` on first run, `npm run build` = rollup,
  `npm run pack` = `streamdeck pack` → `streamdeck\dist\com.bones84.launchstage.streamDeckPlugin`). Check with
  `npx streamdeck validate com.bones84.launchstage.sdPlugin` and `npx tsc --noEmit -p .` in `streamdeck\`.
- User guide: `guide\*.md` + `guide\topics.txt` (order + format notes) + `guide\images\*.png`, built into the app
  as `Guide/...` resources (App.csproj). `make-guide-pictures.cmd` runs `tools\GuidePictures` (renders real windows
  off-screen at 150% with demo data in a temp folder via the `LAUNCHSTAGE_DATA` override in `AppPaths`; one process
  per picture; it also has `test-help` / `test-welcome` for checking those windows). `build-guide.cmd` runs
  `out\LaunchStage.exe --export-guide docs` then headless Edge `--print-to-pdf` → `docs\LaunchStage guide.pdf`.
  **When a feature or its wording changes, update the matching guide topic too** (and retake pictures if a window
  changed).
- **All rights reserved** (owner's requirement: nobody may distribute it without the permission and consent of
  Bones_84). `LICENSE.txt` (owner chose: **free for personal use**, download only from the official GitHub page, no
  sharing/re-uploading/selling/commercial use/modifying/reusing code; copied next to LaunchStage.exe via App.csproj
  Content, and into the Stream Deck plugin by its `pack` script), `src\Directory.Build.props`
  (Product/Company/Copyright on every exe/dll), `App/Services/AppInfo` (Version, Copyright, UseNotice, OpenLicense)
  shown in the launcher footer, Help, Settings,
  the walkthrough's first page, the guide's cover/footer and the CLI help. Keep these on anything new that's shared.
- Testers: `make-tester-package.cmd` → `dist\LaunchStage test.zip`. Owner's requirement: **LaunchStage.exe must be
  in the first folder after extracting**, so everything goes in the zip root (`dist\package`): single-file,
  self-contained, compressed win-x64 publishes (CLI then app; ~13 files, no temp extraction since native WPF dlls
  stay beside the exe), READ ME FIRST, LICENSE, guide PDF, Stream Deck plugin folder, `Uninstall LaunchStage.cmd`,
  and `uninstall-files.txt` listing every file in the zip. Never use `Assembly.Location` (empty in single-file).
- GitHub: **public** repository (owner switched from private to see if people download it), published with GitHub
  Desktop (no git CLI on this PC; GitHub Desktop's own git is at
  `%LocalAppData%\GitHubDesktop\app-*\resources\app\git\cmd\git.exe`). Feedback via GitHub Issues (public: READ ME
  FIRST warns that logs contain window titles). `.gitignore`
  excludes build output, `dist`, `node_modules` and plugin build files; `.gitattributes` forces CRLF. Tester zips go
  on GitHub Releases (pre-release), not in the repo. Never commit personal paths or the owner's data folder.
- Remove LaunchStage (owner: "no need to install but needs a way to remove all left behind files it created"):
  `App/Services/Uninstaller` + `Views/UninstallWindow` (Settings card button, `--uninstall`, tester zip's
  `Uninstall LaunchStage.cmd`). Removes the HKCU Run value (`StartupManager.Remove`), Admin support tasks
  (`AdminTasks.Remove`, plus the empty Task Scheduler folder via COM in `DeleteTasks`), then a temp PowerShell script
  waits for the process to exit and deletes: files listed in `uninstall-files.txt` (written by
  make-tester-package.cmd; nothing else in the folder) and the folder if empty; `%AppData%\LaunchStage` (whole, or only
  Logs/Snapshots/app-path/open-profiles when profiles are kept; refuses a folder not named LaunchStage); the guide temp
  copy; itself. **Anything new LaunchStage writes to the PC must be added to Uninstaller.**
- There are no automated tests yet. After changes, build and check `%AppData%\LaunchStage\Logs\debug_log.txt`.

## Projects

| Project | Output | Purpose |
| --- | --- | --- |
| `src/LaunchStage.Core` | class library | The engine: models, window finding/moving/closing, activation, closing, games |
| `src/LaunchStage.Cli` | `LaunchStageCli.exe` | `activate`, `close`, `resnap`, `capture`, `profiles`, `windows [--all]`, `monitors`, `folder` |
| `src/LaunchStage.App` | `LaunchStage.exe` (RootNamespace `LaunchStageApp`) | WPF launcher, tray, editor, dialogs |
| `streamdeck` | `.streamDeckPlugin` | Elgato Stream Deck plugin (TypeScript, `@elgato/streamdeck` 3.x, SDKVersion 3, Stream Deck 7.1+, Node 24 runtime) |

### Core (`LaunchStage.Core`)
- `Models/` — `Profile`, `AppEntry`, `WindowPosition` (position relative to a monitor), `AppSettings`, `Rect`, enums.
- `Desktop/` — Win32: `WindowFinder` (`GetAppWindows(includeNotInTaskbar)`, `GetEveryTitledWindow`),
  `WindowMover`, `WindowCloser`, `Monitors` (`Resolve`, `ToScreenRect`, `FromRect`), `NativeMethods`.
- `Engine/` — `ProfileActivator` (snapshot → close/minimize others → launch one at a time with a readiness
  check → place → re-apply pass → minimize extra windows), `ProfileCloser` (move windows back to profile spots →
  close one at a time respecting save prompts → restore kept apps), `WindowCloseRunner`, `WindowMatcher`
  (`Matches`, `PickBest`, `AssignWindows`, `Siblings`), `Launcher` + `UnelevatedLauncher`, `ProfileCapture`,
  `BrowserPrivacy` (private/incognito windows), `ProfilePin`, `WindowSnapshot`, `ProtectedApps`.
- `Games/GameLibrary.cs` — installed Steam (libraryfolders.vdf + appmanifest) and Epic (.item manifests) games
  plus manual ones; history in `%AppData%\LaunchStage\games.json`.
- `Storage/` — `ProfileStore` (JSON, camelCase, string enums), `AppPaths`.

### App (`LaunchStage.App`)
- `App.xaml.cs` — args (`--tray`, `--exit`, `--activate "X"`, `--close "X"`, `--resnap "X"`, `--toggle "X"`,
  `--games`, `--setup-admin`, `--remove-admin`, `--startup`; requests go through one `(Command, Argument)` that is
  handed to the running copy / admin copy or run via `HandleCommand`), single instance, admin hand-off, settings,
  after-activation behaviour, game picker trigger, hotkeys (`ReloadHotkeys`, `ToggleProfile`), writes `app-path.txt`.
  Unhandled errors while exiting are only logged (a message box there used to block `--exit`).
- `Services/` — `ActivationRunner` (PIN gate → runs engine on a background thread with `StatusPopup`),
  `SingleInstance` (mutex + named pipe with ACL), `AdminTasks` (scheduled tasks `LaunchStage\Startup` and
  `LaunchStage\Run`), `TrayIcon` (WinForms NotifyIcon), `PinGate`, `StartupManager`, `ThemeManager`, `Dialogs`...
- `Views/` — `MainWindow` (profile cards), `ProfileEditor` (monitor preview with draggable boxes + per-app
  settings), `ProfileDialog` ("Name, look & options": name/icon/color/options/PIN/close choices/re-pick windows),
  `SettingsWindow`, `StatusPopup`, `PinDialog`, `CommandsDialog` (Stream Deck command lines), `WindowPicker`,
  `GamePicker`, `HelpWindow` (guide topics + search; `ShowTopic(id)`; `GuideDocument` renders a topic as a themed
  FlowDocument; reopens fresh if a modal dialog disabled it), `WelcomeWindow` (first-run walkthrough, 5 pages,
  `AppSettings.WelcomeShown`, shown from `App.ShowMainWindow` — never at a tray-only start).
- Guide code: `Services/Guide.cs` (parser for the guide's markdown subset: `#`, `##`, `1.`, `-`, `![caption](file)`,
  `> tip`, `| table |`, inline `**bold**`, `*italic*`, `` `key` ``, `[text](topic:id)`) and `Services/GuideHtml.cs`
  (one printable page). Small round **?** buttons (`HelpLink` style, topic id in `Tag`, each window has a one-line
  `Help_Click`) sit next to tricky sections in ProfileDialog, SettingsWindow and ProfileEditor. Launcher: **Help**
  button and `F1`; tray: **Help**. SettingsWindow.Save must copy every AppSettings field it doesn't edit (it
  builds a new object), e.g. `WelcomeShown`.
- `Theme/Theme.xaml` — styles plus the dark default colors (Bones Caption Studio palette: #121212 / #1E1E1E / teal
  #00ADB5). `Services/ThemeManager` replaces every color brush at runtime: Dark / Light / System (Match Windows, via
  `AppsUseLightTheme` + `SystemEvents.UserPreferenceChanged`), each with a red-green color-blind version; raises
  `Changed`; `DarkTitleBar.UpdateAll()` flips title bars; the tray menu reads `ThemeManager.IsDark` when it opens.
  **Always use `{DynamicResource ...}` for colors** (a StaticResource keeps the old color after a switch); in code
  use `SetResourceReference` or redraw on `ThemeManager.Changed` (ProfileEditor does). Styles exist for Button,
  AccentButton, DangerButton, CheckBox, RadioButton, IconTile, TextBox, PasswordBox, ListBox, ScrollBar,
  ContextMenu, ToolTip. **No ComboBox style** — use radio buttons/tiles.
- Launcher layouts (`AppSettings.LauncherLayout`: Cards, SmallCards, List, Tiles): `MainWindow.xaml` resources hold
  `Card_<layout>` / `New_<layout>` templates and the shared `CardMenu` (x:Shared=False); `MainWindow.LayoutSelector`
  and `ApplyLayout()` pick them (List uses a StackPanel, the rest a WrapPanel). Header quick buttons and
  Settings > Look switch it (`App.SetLayout`).

## Data (all in `%AppData%\LaunchStage`)
`Profiles\*.json`, `Profiles\Images\`, `Snapshots\` (pre-profile window spots), `Logs\debug_log.txt`,
`settings.json`, `games.json`, `app-path.txt` (LaunchStage.exe path, written at start), `open-profiles.json` (names of
open profiles, `App/Services/OpenProfilesFile`: checked every 2 s, rewritten only on change, deleted on exit).

## Gotchas (learned the hard way)
- WPF projects drop the implicit `System.IO` using: add `using System.IO;` in App files that use File/Path.
- `System.Windows.Rect` vs `LaunchStage.Core.Models.Rect` and `System.Windows.WindowState` vs Core `WindowState`
  clash in WPF files: use `var` or aliases (`using ScreenRect = ...; using AppWindowState = ...;`).
- Inside lambdas declared as `(hWnd, _) =>`, `out _` refers to that parameter: use named out variables.
- The app uses WinForms only for the tray icon (`UseWindowsForms`, with the `System.Windows.Forms` and
  `System.Drawing` global usings removed; NoWarn WFO0003).
- Per-monitor DPI v2 manifest; windows have a ~7 px invisible border on left/right/bottom (captured X is often -7).
- When LaunchStage runs elevated it starts normal apps unelevated (`UnelevatedLauncher`, shell-token duplication);
  apps marked `runAsAdmin` start elevated.
- Nothing is ever force-closed: windows get WM_CLOSE one at a time, waiting while the app shows a dialog.
- Files use CRLF line endings.
- Keep user-facing text plain and friendly (no jargon); log useful detail to the debug log.

## Status
Working and tested by the owner: profiles, capture, activation/closing, exact multi-monitor positions,
never-close list, close-with-profile choices, restoring kept apps, admin support, private profile PIN,
Stream Deck command lines, Profile Editor, game picker (profile option + tray "Play a game...").

Built but **not yet tested**: private/incognito browser windows, moving windows back before closing, per-window
close choices for multi-window apps; tray "Close a profile" listing only open profiles (open = snapshot file exists
and one of its apps has a window, `WindowSnapshot.IsProfileOpen`). Tested and working: several windows of one
browser in a profile (browser titles are just the current page, so `ProfileActivator.WaitForReadyWindow` only prefers the saved title for
2s, then takes any new window of that browser — before, entry 1 waited 30s for a window showing entry 2's page).

Tested by the owner, works (with Brave): websites per browser window. `AppEntry.Websites`; `BrowserPrivacy.ArgumentsFor`
adds `--new-window` (Firefox `-new-window`, extra sites `-new-tab`) or the private flag, then quoted URLs
(`NormalizeWebsite` adds https://). In `ProfileActivator`, an entry with websites only reuses an open window with the
same saved title, skips the "other window" wait, and only accepts a window that wasn't open before it launched.
`WindowMatcher.SameTitle` ignores a leading "(7) " notification counter (used in PickBest and ProfileCloser).
`App/Services/BrowserAddress` reads the address bar via UI Automation (first Edit/ComboBox with a dotted value;
Chromium = Edit, Firefox = ComboBox; verified on Firefox, not yet on Brave); used by ProfileDialog (picking windows),
ProfileEditor "+ Open window" and the "Use the site it's showing now" button. The CLI `capture` doesn't fill websites.
ProfileDialog now scrolls as a whole (its Apps section used to get squeezed to nothing when the lower card grew).

TransparentTwitchChatWPF (overlay app with a main window plus child overlay windows): closing a child first
deletes it. Cause found: its main window has an **empty title** (WPF HwndWrapper, 320x500, no owner), and
`WindowFinder` skips untitled windows. Fix (**tested by the owner, works**): `ProfileCloser.MainWindowOnlyApps`
— for these apps, closing uses `WindowFinder.GetTopLevelWindows(process)` (visible, no owner, not WS_CHILD, title
not required) and closes only that main window; the app closes its overlays itself. `windows --all` now lists
untitled main windows too. Some overlay windows are owned by hidden HwndWrapper windows, but in testing two unowned
top-level windows were found and the untitled main one was correctly picked (overlays' saved titles are skipped).

Tested by the owner, works: Duplicate profile (card right-click → Duplicate; `ProfileStore.Duplicate`: JSON clone,
`UniqueName` → "X (2)", Favorite and hotkeys off, PIN kept; PIN asked first; then ProfileDialog opens on the copy).

Tested by the owner, works: global hotkeys and re-snap.
- Hotkeys (`App/Services/Hotkeys.cs`): `HotkeyManager` registers via `RegisterHotKey` on a message-only `HwndSource`
  (MOD_NOREPEAT; actions run through `Dispatcher.InvokeAsync`); `Pause`/`Resume` while a hotkey box has focus.
  `HotkeyText` formats/parses "Ctrl+Alt+1" (needs Ctrl/Alt/Win, or F13–F24 alone; Shift alone is refused),
  `FindOwner` blocks duplicates inside LaunchStage, `Attach(TextBox)` makes a press-the-keys box. Saved as
  `Profile.Hotkey` (owner chose: toggle — opens, or closes when `WindowSnapshot.IsProfileOpen`), `Profile.ResnapHotkey`
  (owner chose: one re-snap key per profile) and `AppSettings.GamePickerHotkey`. `App.ReloadHotkeys()` runs at
  startup, after settings change and on every `MainWindow.RefreshProfiles`; conflicts with other programs are shown
  once each. Hotkeys are released in ExitApp/PrepareForRestart/OnExit.
- Re-snap: `Core/Engine/ProfileResnapper` (AssignWindows + WindowMover.Apply, nothing opened/closed). Triggers: per
  profile hotkey, card menu and tray "Put windows back" (open profiles only), `LaunchStage.exe --resnap "X"` (also
  handed to the admin copy), `LaunchStageCli resnap "X"`, CommandsDialog. `ActivationRunner.ResnapAsync` (no PIN,
  it only moves windows; StatusPopup `resnapping` mode).
Browsers known to `BrowserPrivacy` (websites + private windows): chrome, brave, msedge, firefox, vivaldi, opera,
chromium, arc, thorium, zen, librewolf, waterfox, floorp, mullvadbrowser. Yandex left out on purpose: its process is
the generic `browser.exe`.

Built, **not tested on real hardware** (the owner has a Soomfon pad, not an Elgato Stream Deck, and no Stream Deck
app installed): the Stream Deck plugin. Passes `streamdeck validate`, type-checks, packs; its file helpers were run
with Node against real data. Actions: `com.bones84.launchstage.profile` (settings `profile`, `mode` toggle/open/close;
2 states, state 1 = open, polled every 2 s from `open-profiles.json`, only changes sent), `.resnap` (`profile`),
`.games`. All work is done by spawning `LaunchStage.exe` with the command-line options (detached), found through
`app-path.txt`. Settings panels use sdpi-components v4 from its CDN (as Elgato's template does); the profile
drop-down is a `datasource="getProfiles"` answered in `src/launchstage.ts`. Images are drawn by
`streamdeck/make-images.ps1` (Segoe Fluent Icons glyphs, app colors). `--toggle` / `--games` also suit the owner's
Soomfon (CommandsDialog shows them).

## Roadmap (owner agreed, in order)
1. ~~Duplicate profile~~ (done).
2. ~~Global hotkeys~~ (done).
3. ~~Re-snap~~ (done).
4. ~~Stream Deck plugin~~ (built; untested on hardware).
5. ~~Launcher polish~~ (done, tested by the owner): layouts Cards / Small cards / List / Big tiles, and colors
   Dark / Light / Match Windows (`AppSettings.Theme` = "Dark", "Light" or "System"). In red-green assist mode the
   danger color (Delete, Close App) is orange, like every other "problem" color (it used to be white).
6. Easy instructions (owner asked: "do it all"; built, **not yet tested by the owner**): built-in Help with
   12 topics and pictures, ? links, first-run walkthrough, and the web page + PDF in `docs\`.
7. Released: public GitHub repository with release v0.2.0 (pre-release, "LaunchStage test.zip"), free for personal
   use. Remove LaunchStage built; its file cleanup was tested on throwaway copies, not yet end-to-end by the owner.
   Next: watch downloads/Issues, fix what testers report.
Later: voice commands (Windows built-in speech, fixed phrases, push-to-talk), release packaging
(self-contained publish, Velopack installer + auto-update, GitHub Releases, uninstall cleanup of scheduled tasks
and startup entry, version/About screen).
