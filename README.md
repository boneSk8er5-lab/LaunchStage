# LaunchStage

Copyright (c) 2026 Bones_84. All rights reserved. LaunchStage is **free for personal use** but not open-source: it
may not be copied, re-uploaded, shared, sold, modified or reused without the permission and consent of Bones_84, and
seeing the code here doesn't give permission to use it. See [LICENSE.txt](LICENSE.txt).

**Download:** get the latest zip from this page's **Releases** (on the right), unzip it, and follow
*READ ME FIRST.txt* inside. Found a problem or have an idea? Open an **Issue**.

One press puts your PC into a saved workspace: the right apps open, apps you don't need close or minimize, and every window lands on the right monitor in the right spot.

## What's in this folder

| Part | What it is |
| --- | --- |
| `src\LaunchStage.Core` | The engine: finds, closes, opens and positions windows; reads and writes profiles |
| `src\LaunchStage.App` | **LaunchStage.exe**, the app: launcher window, tray icon, profile dialogs |
| `src\LaunchStage.Cli` | **LaunchStageCli.exe**, the command-line tool |
| `streamdeck` | The Stream Deck plugin (built separately with `build-streamdeck.cmd`) |
| `guide` | **The user guide** (easy step-by-step topics and their pictures), built into LaunchStage's Help |
| `docs` | The guide as a web page and PDF to share (`guide.html`, `LaunchStage guide.pdf`), made by `build-guide.cmd` |
| `tools\GuidePictures` | Takes the guide's pictures (run `make-guide-pictures.cmd`) |

**New to LaunchStage? Read the guide:** click **Help** in LaunchStage (or press F1), or open
`docs\LaunchStage guide.pdf`. This README is the technical reference.

## Build it

1. Install the free **.NET 10 SDK** for Windows x64 (one time): https://dotnet.microsoft.com/download/dotnet/10.0
2. If LaunchStage is running, right-click its tray icon and choose **Exit**.
3. Double-click `build.cmd`. Everything lands in the `out` folder.

If the build fails, copy the red error lines and send them to Claude.

## Send it to testers

Double-click `make-tester-package.cmd`. It makes `dist\LaunchStage test.zip`, which runs on any Windows 10 or 11 PC
without installing anything (.NET is included). Inside: the LaunchStage folder, **READ ME FIRST.txt** (simple install
steps and the sharing rules, from `tester\READ ME FIRST.txt`), LICENSE.txt, the guide PDF and the Stream Deck plugin.
Run `build-guide.cmd` and `build-streamdeck.cmd` first so those two are up to date. The first time, `dotnet` downloads
Microsoft's .NET runtime packs (one time, about 150-200 MB).

## GitHub

The code is in a **public** GitHub repository (anyone can see it; the license still reserves all rights).
`.gitignore` leaves out everything the build scripts make (`out`, `dist`, `bin`, `obj`, `node_modules`...), and
`.gitattributes` keeps Windows line endings. The download zip isn't stored with the code: attach
`dist\LaunchStage test.zip` to a GitHub **Release** (marked as a pre-release while it's an early version).

## Use the app

Run `out\LaunchStage.exe`.

- **New profile:** arrange your windows, click the dashed **New profile** card, name it, pick an icon and color, tick the windows that belong, and save.
- **Activate:** double-click a profile card. A small popup in the corner shows progress and asks what to do if an app won't close.
- **Right-click a card** to activate, close, put its windows back, edit, duplicate, favorite, export or delete it.
- **Duplicate:** makes a full copy named like "Stream (2)" (apps, window spots, websites, options, icon, image and
  PIN) and opens its *Name, look & options* window so you can rename it. The copy isn't a favorite and has no
  hotkeys. A private profile asks for its PIN first, and the copy keeps the same PIN.
- **Close profile:** closes the profile's apps one at a time (asking to save first). Pick which apps it closes in the profile window under "When you close this profile, close".
- **Tray icon:** double-click opens LaunchStage; right-click lists your profiles. **Close a profile** and **Put
  windows back** list only profiles that are open right now (greyed out when none are). Closing the window keeps LaunchStage in the tray; **Close App** exits completely.
- **Settings:** Start with Windows, tray icon, red-green vision assist, look (colors and layout), hotkeys, the Never
  close list, import and export.
- **Look:** in **Settings > Look**, pick the colors (**Dark**, **Light**, or **Match Windows**, which follows Windows'
  light/dark app setting and switches with it) and the launcher layout: **Cards**, **Small cards** (icon and name),
  **List** (one row per profile) or **Big tiles** (large and easy to hit). The four small buttons at the top of the
  launcher switch the layout in one click. Everything changes right away, including the tray menu and title bars, and
  red-green vision assist works with both color themes.

### Stream Deck, hotkeys and shortcuts

Point any button or shortcut at:

```
C:\path\to\out\LaunchStage.exe --activate "Stream"
```

No console window appears. If LaunchStage is already running, the running copy does the work.

To close a profile from a button: `LaunchStage.exe --close "Stream"`

To put a profile's windows back in their spots: `LaunchStage.exe --resnap "Stream"`

One button that opens the profile, or closes it when it's open: `LaunchStage.exe --toggle "Stream"`

To open the game picker: `LaunchStage.exe --games`

Right-click a card and choose **Stream Deck commands...** to copy any of these, ready to paste. They work with any
macro pad or launcher that can open a program with arguments (Stream Deck, Soomfon and similar pads, shortcuts).

### Stream Deck plugin (Elgato Stream Deck app 7.1 or newer)

For a real Stream Deck there's also a LaunchStage plugin with three buttons:

| Button | What it does |
| --- | --- |
| **Profile** | Pick a profile and what pressing does: *Open it, or close it if it's open* (default), *Open it* or *Close it*. The button lights up teal while the profile is open. |
| **Put windows back** | Pick a profile; puts its open windows back in their spots. |
| **Play a game** | Opens the game picker. |

To install: double-click `streamdeck\dist\com.bones84.launchstage.streamDeckPlugin` on the PC with the Stream Deck
app, then drag the buttons from the **LaunchStage** group onto your Stream Deck. Open LaunchStage once first (it
notes where it's installed so the plugin can find it), and keep it running (the tray is fine).

To rebuild the plugin after changes: double-click `build-streamdeck.cmd` (needs Node.js from https://nodejs.org; the
first run downloads Elgato's free plugin tools into the `streamdeck` folder).

## Hotkeys

Key combinations that work anywhere in Windows while LaunchStage is running (even hidden in the tray).

- **Per profile**, in its *Name, look & options* window:
  - **Open / close this profile:** opens the profile, or closes it if it's already open (closing still asks to save,
    one app at a time).
  - **Put its windows back:** re-snap (see below).
- **Game picker**, in **Settings > Hotkeys**.

Click a box and press the keys, like **Ctrl+Alt+1**. A hotkey needs Ctrl, Alt or Win with a key; F13 to F24 (common on
macro pads) also work alone. **Clear** removes it. LaunchStage won't let two of its own hotkeys share a combination,
and if another program already uses one, it tells you so you can pick another. A duplicated profile starts without
hotkeys. Saved as `hotkey` / `resnapHotkey` in the profile and `gamePickerHotkey` in settings.json.

## Re-snap: put windows back

Dragged windows around during a session? Re-snap puts an open profile's windows back in their spots without opening,
closing or minimizing anything (apps that aren't open are skipped). Use the profile's re-snap hotkey, right-click its
card or the tray icon and choose **Put windows back**, or a Stream Deck button with `--resnap "Stream"`.

## Remove LaunchStage

**Settings > Remove LaunchStage...** (or `Uninstall LaunchStage.cmd` in the downloaded LaunchStage folder, which runs
`LaunchStage.exe --uninstall`) asks first, then removes everything LaunchStage added: the Start with Windows entry,
the Admin support scheduled tasks and their Task Scheduler folder, `%AppData%\LaunchStage` (profiles, settings and
game history only if "Also delete my profiles..." is ticked, which it is by default), the guide's temp copy, and the
program files. Program files are only removed when `uninstall-files.txt` (the list of LaunchStage's own files, made by
`make-tester-package.cmd`) is next to LaunchStage.exe; it deletes just those files and the folder if that leaves it
empty. The deleting is done by a short script that waits for LaunchStage to exit. A copy run from `out\` keeps its
folder (there's no list there). The Stream Deck plugin is removed in the Stream Deck app.

## Help and the guide

- **Help** at the top of the LaunchStage window (or `F1`, or **Help** in the tray menu) opens the guide: easy,
  step-by-step topics with pictures, and a search box. The small round **?** next to some options opens the guide at
  the right topic.
- The first time the LaunchStage window opens, a short **Getting started** walkthrough shows how to make, open and
  close a profile. Open it again any time with **Getting started tour** in Help.
- **Open as web page** (in Help) opens the whole guide in your browser, where you can print it or save it as a PDF.

### Updating the guide

1. Edit the topic files in `guide\` (the format is explained at the top of `guide\topics.txt`).
2. If a window looks different, double-click `make-guide-pictures.cmd` to take new pictures. They're made with
   made-up demo profiles, so your own profiles, windows and games never appear in them.
3. Double-click `build.cmd` (puts the guide into LaunchStage), then `build-guide.cmd` (refreshes `docs\guide.html`
   and `docs\LaunchStage guide.pdf`).

## Use the command-line tool

From a terminal in the `out` folder:

```
.\LaunchStageCli capture "Code"       Save the windows open right now as a profile
.\LaunchStageCli activate "Code"      Close, open and arrange everything for that profile
.\LaunchStageCli close "Code"         Close that profile's apps (asks to save first)
.\LaunchStageCli resnap "Code"        Put that profile's open windows back in their spots
.\LaunchStageCli profiles             List saved profiles
.\LaunchStageCli windows              List open app windows
.\LaunchStageCli monitors             List monitors
.\LaunchStageCli folder               Open the Profiles folder
```

## Where things are saved

| What | Where |
| --- | --- |
| Profiles (one file each) | `%AppData%\LaunchStage\Profiles\` |
| Profile images | `%AppData%\LaunchStage\Profiles\Images\` |
| Settings | `%AppData%\LaunchStage\settings.json` |
| Debug log | `%AppData%\LaunchStage\Logs\debug_log.txt` |
| Where LaunchStage.exe is (for the Stream Deck plugin) | `%AppData%\LaunchStage\app-path.txt` |
| Profiles open right now (for the Stream Deck plugin; removed when LaunchStage exits) | `%AppData%\LaunchStage\open-profiles.json` |

Exported profiles are `.lsprofile` files (a zip of the profile and its images). Importing a name that already exists adds " (2)".

## How activation works

1. **Close phase** (when the profile closes other apps): apps not in the profile close one at a time, exactly like clicking X. If an app asks to save, LaunchStage waits for your answer before moving on. If you press Cancel, you choose **Try again**, **Leave it open & continue** or **Stop profile**. Nothing is ever force-closed.
2. **Minimize phase** (when the profile minimizes other apps).
3. **Launch & position**: apps already open are reused and moved; missing apps are opened and moved once their window appears. Games launch last.
4. **Settle pass**: a few seconds later, any window that moved itself (or was a splash screen) is put back.

## Editing a profile by hand

Until the full Profile Editor arrives, per-app details live in the profile's `.json` file (open it from Settings, **Open profiles folder**). Useful fields per app:

| Field | What it does |
| --- | --- |
| `name` | Display name |
| `type` | `Program` or `Game` |
| `behavior` | `LaunchAndPosition`, `PositionOnly` or `LaunchOnly` |
| `path` | What to open: an .exe, shortcut, URL, or a link like `steam://rungameid/1145360` |
| `arguments` | Launch arguments, e.g. a project folder for VS Code |
| `websites` | Browsers only: the sites this window opens on, e.g. `["https://dashboard.kick.com"]` |
| `processName` | The process whose window belongs to this app (e.g. `javaw` for Minecraft) |
| `titleContains` | Only match windows whose title contains this text |
| `runAsAdmin` | Start it as administrator (asks each time until admin support is added) |
| `autoStart` | Games only: launch the game when the profile activates |
| `launchDelaySeconds` | Wait before opening this app |
| `launchTimeoutSeconds` | How long to wait for its window (default 30) |

## Admin support

Apps that run as administrator can only be moved or closed by a program that also has administrator rights.
Turn on **Settings > Admin support** and Windows asks for permission once. LaunchStage then creates two
scheduled tasks (in Task Scheduler under **LaunchStage**) that start it with administrator rights without asking again:

| Task | What it does |
| --- | --- |
| `LaunchStage\Startup` | Starts LaunchStage at sign-in (enabled only when Start with Windows is on) |
| `LaunchStage\Run` | Starts the administrator copy whenever you open LaunchStage normally |

Apps you don't mark **Run as administrator** (in the profile window) still start as normal apps.
Turning Admin support off removes both tasks.

## Profile Editor

Right-click a profile and choose **Edit layout & apps...**. Your monitors are drawn to scale with a box for each
window in the profile:

- Drag a box to move the window, drag its bottom-right corner to resize it. Boxes snap to screen edges and halves
  (hold **Alt** to stop snapping). After clicking a box, the arrow keys nudge it 1 px (Shift: 10 px).
- Click an app in the list (or its box) to change its program, arguments, monitor, normal / maximized / minimized,
  exact position, admin, close-with-profile, private window, launch delay and matching rules.
- **+ Open window** adds a window that's open now; **+ Program** adds an .exe or shortcut. **Move up / down** sets
  the opening order; **Remove** takes an app out.
- **Move it there now** moves that app's open window to the spot to try it out; **Use its current spot** copies
  where it is now; **Arrange open windows now** puts every open window of the profile in place without opening or
  closing anything.
- Nothing is saved until **Save profile**. **Name, look & options...** opens the older window for the name, icon,
  color, other-apps choice, private PIN and re-picking every window at once.

## Game picker

Tick **Suggest a game when this profile is ready** in a profile's *Name, look & options* window. When that profile
finishes, a **Pick a game** window pops up: double-click a game (or press Enter) to start it, or press **Not now**.
You can also open it any time from the tray: **Play a game...**

- Installed **Steam** and **Epic** games show up by themselves. **+ Add game...** adds anything else (an .exe or
  shortcut).
- Favorites come first, then what you played most recently. Right-click a game to favorite it, hide it, or remove
  a game you added.
- Type to search; the arrow keys move through the list.
- Your added games, favorites, hidden games and play history are kept in `%AppData%\LaunchStage\games.json`.

## Apps with several windows

Some apps (like Transparent Twitch Chat) open several windows, and closing the wrong one first can delete it
(e.g. an overlay asks "remove this window?"). In the profile window, under **When you close this profile, close**,
an app like that gets one checkbox per window. Tick only the main window: LaunchStage closes just that one and lets
the app close its other windows itself. Windows are told apart by their title (saved as `capturedTitle`), so a
profile made before this version needs its windows picked again once (**Replace this profile's apps**).

**Transparent Twitch Chat** is handled for you: when a profile closes it, LaunchStage finds its main window (the
one that isn't attached to any other window, even though it has no title) and closes only that. The app then
closes its chat overlays itself, so none of them get deleted.

Windows that hide from the taskbar (common for overlay apps) are listed too, marked *not in taskbar*. If an app's
window still doesn't show up, run `LaunchStageCli windows --all`: it lists every window, hidden ones included, and
says why each one is skipped.

**Moved things around during a session?** With *Move windows back to their profile spots before closing them* on
(the default), closing a profile first puts each of its windows back where the profile has it, then closes it. Apps
that remember their own window positions then remember the profile's layout, not where you dragged them.

## Websites for browser windows

Each browser window in a profile can open on its own websites. In **Edit layout & apps...**, click a browser window
and fill in **Websites**: one per line, each opens as a tab in that window (`kick.com` is fine; `https://` is added
for you). **Use the site it's showing now** reads the address from the open window. When you add a browser window
(**+ Open window**, or picking windows in *Name, look & options*), LaunchStage reads the site it's showing and fills
the box in for you.

A browser window with websites always opens as its own new window on those sites. If it's already open (same
window title, ignoring a notification count like "(7)"), it's reused instead. Leave the box empty and the browser
shows its usual start pages, like before. It's saved per window as `websites`.

## Private / incognito browser windows

When a profile has browser windows (Chrome, Brave, Edge, Firefox, Vivaldi, Opera / Opera GX, Chromium, Arc, Thorium,
Zen, LibreWolf, Waterfox, Floorp, Mullvad Browser), the profile window lists them
under **Open these browser windows as private / incognito**. Ticked windows are opened with the browser's private
switch (`--incognito`, `--inprivate` or `-private-window`), and a private entry only ever uses or closes private
windows, while normal entries leave private windows alone. It's saved per window as `privateWindow`.

Edge and Firefox show "InPrivate" / "Private Browsing" in the window title, so those are ticked automatically when
you pick the window. Chrome and Brave don't, so tick them yourself. LaunchStage remembers which windows it opened
privately while it's running; after LaunchStage restarts, a Chrome or Brave private window that's already open is
treated as a normal one, and the profile opens a fresh private window.

## Private profiles

Tick **Private profile** in the profile window and type a 4-digit PIN twice. After that, the PIN is asked for
every time the profile is opened, closed, edited, exported or deleted: from the card, the tray, the Stream Deck
(`--activate` / `--close`) or the command-line tool. The card shows a lock and hides its app list.
Five wrong tries lock the profile for 60 seconds.

Only a salted, scrambled copy of the PIN (`pinHash`, `pinSalt`) is saved, never the PIN itself. This keeps other
people using LaunchStage out of the profile; it doesn't encrypt the profile file. If you forget the PIN, delete the
profile's file from the Profiles folder and make it again.

A Stream Deck button for a private profile still works: LaunchStage pops up the PIN box when you press it.

## Known limits in this build

- Store apps (Settings, Calculator) are moved if open but not launched.
- Apps whose .exe path changes with updates (Discord) may need `path` edited to their launcher, e.g. `%LocalAppData%\Discord\Update.exe` with `arguments` set to `--processStart Discord.exe`.
- Exclusive-fullscreen games ignore positioning; use borderless windowed.
