# DragFlow – where it is today and where it could go

*Written 22 October 2026. Source lives in `Desktop\DragFlow Project`; the copy you run is in `%LOCALAPPDATA%\DragFlow`.*

---

## Status: Tier 7 built (version 1.5, 27 October 2026)

**How it was checked:** 615 automated checks pass with both compilers. They cover the guard (restarts with longer waits, giving up, leaving after Exit, updates and restarts), the sign-in task's definition, every dashboard page building, search, saving with Undo, AI cost per month and the limit, rule sentences and Try it, the script editor, and script tests and approval. 18 live checks with the real mouse pass. A picture of every page in light and dark mode was checked by eye. A fifth independent code review found 18 problems, all fixed; the important ones have tests. The worst: after an update the old guard would have kept the old program file in use and blocked the next update; the guard could bring DragFlow back after Exit in some cases; an error inside the dashboard could have closed the tray app; there were ways around script approval. Version 1.5 is installed, and "always running" was tried on it: DragFlow ended in Task Manager came back by itself.

| # | Item | Status |
|---|---|---|
| Always running | Sign-in task, guard, Exit choice | Done. The sign-in task is created by DragFlow itself for your account only (no administrator rights; checked on this PC first). The guard is DragFlow.exe started as "--guard". It restarts DragFlow after 5 s, 30 s and 2 min, gives up after 5 restarts in 10 minutes, and never fights an Exit, an update or an uninstall. Several starts at sign-in (task, Run entry, guard) end in one DragFlow, quietly. |
| 7.1 | Home | Done: on/off and pause, health (mouse watcher, guard, rules, AI cost), your week, quick switches, recent activity with Undo and Show |
| 7.2 | Dial | Done: a live picture of the dial per kind of file (drawn by the dial's own code), the actions on each slice, always-first order, shown and hidden actions, colours, size, animations, preview, learning, ranking |
| 7.3 | AI and keys | Done: key (encrypted), Test connection, model, usage per month with estimated cost, monthly limit (AI actions pause when reached). Other connections: explained, not built (a OneDrive sign-in) |
| 7.4 | Scripts studio | Done: list with approval state, editor with line numbers and colours, five templates, Save, Approve, Test on a file (full output), Delete to the Recycle Bin. Scripts must be approved (fingerprinted) before they run, from the dial too |
| 7.5 | Rules | Done: each rule as a sentence, an editor with every condition and "When" (as files arrive, hourly, daily, weekly), Try it |
| 7.6 | Recipes | Done: steps, Record what I just did, Run on files |
| 7.7 | Shapes and shortcuts | Done: actions per shape, a practice pad, the shortcut (press the keys), right-click entry, pen and touch |
| 7.8 | Apps and quiet | Done: full screen, programs (with a list of running ones), folders, other programs, shelf, pause for an hour or until tomorrow |
| 7.9 | Profiles | Done: list and form instead of a table |
| 7.10 | Backup and updates | Done |
| 7.11 | Privacy and data | Done: each kept thing with Clear |
| 7.12 | Help | Done: keyboard help, searchable log, problem report, classic settings window, reset all settings |
| 7.13 | Search | Done: finds pages by any setting's name |

**Not done:** a code-signing certificate (it costs money). Translations of the new texts (the template has them). Live colour change while the dashboard is open (it follows Windows when opened).

---

## Status: Tier 6 built (version 1.4, 27 October 2026)

**How it was checked:** 580 automated checks pass with both compilers, including the AI helper against a pretend Anthropic API on this PC, encryption round trips and tampering, QR codes scanned back with OpenCV (versions 1 to 11), the phone server, scheduled rules over a simulated week, and shapes and touch with a fake mouse. 18 live checks with the real mouse pass, including drawing a Z mid-drag and dropping on the offered Zip (this first failed: a Z drawn right after a straight drag was measured together with that drag; fixed, with a test). A fourth independent code review found 17 problems (the worst: a recorded recipe could call itself and crash DragFlow; "To phone" zipped folders on the screen thread and could leave the copies behind; recorded recipes could send files to the AI from a folder rule); all are fixed, with tests for the important ones. Version 1.4 is installed.

| # | Item | Status |
|---|---|---|
| 6.1 | AI helper | Done: AI name, summary, describe, translate, table to CSV, Ask AI, e-mail draft, on the new Smart slice. Off until switched on with your own Anthropic API key (Settings › AI & more), which is stored encrypted for your Windows account (DPAPI) and never in settings, backups or logs. Claude Opus 5.5 by default (Sonnet 5.5 or Haiku 5.5 to choose), with Anthropic's automatic fallback when a request is declined. Pictures are made smaller before sending; Office files go as PDF; at most 20 files at a time. Tested against a pretend API on this PC; **not yet tried with a real key** (none on this PC). |
| 6.2 | Smart names | Done: photo date taken, PDF and Office titles (junk titles such as "Microsoft Word - …" ignored), invoices and receipts by date and number from OCR. Number by date for photos. Undoable. |
| 6.3 | Similar photos | Done: a look-alike fingerprint finds resized or re-saved copies; the largest stays, the others go to "Similar photos" (undoable). Heavily cropped copies are not caught. |
| 6.4 | To phone | Done: a QR code (DragFlow's own encoder, checked by scanning it back with OpenCV) for a link with 128 random bits on this PC's network; only the chosen files; stops when the window closes or after 15 minutes. |
| 6.5 | Share link | Done as: copy into OneDrive › DragFlow shared, then Windows' Share window, whose *Copy link* gives the link. A link straight from DragFlow would need a OneDrive sign-in inside DragFlow. |
| 6.6 | Protect | Done: Encrypt… / Decrypt (AES-256, PBKDF2 with 300 000 rounds, tamper check before anything is written; originals never changed), Checksum compares with one copied from a web page or a SHA256SUMS file. |
| 6.7 | Scripts as actions | Done: .ps1, .py, .bat, .cmd in the scripts folder, with a "# DragFlow: name=…; types=…" line; Cancel stops them; the last printed line is the result. Not undoable (their own code). |
| 6.8 | Scheduled rules | Done: "daily 18:00", "weekly sun 18:00", missed times made up, downloads-only (Windows' internet mark), and rules that run a recipe. |
| 6.9 | Record a recipe | Done: tray › Save last actions as a recipe… finds the chain just done (each step working on the previous step's result). |
| 6.10 | Preview | Done: Windows' thumbnail in the middle of the dial, and "1 file · 2.4 MB · 4032 × 3024" (or pages, or length) under it. |
| 6.11 | Gestures | Done: Z, V, L, N after the dial has gone; the action appears beside the pointer, never under it, and disappears after 3 seconds. Actions chosen in Settings. Tested live with the real mouse. |
| 6.12 | Pen and touch | Done: Windows' pen/touch mark on mouse messages gives a larger dial that lets the pointer wander further. Not tried on a real touch screen (this PC has none). |

---

## Status: Tier 5 built (version 1.3, 23 October 2026)

**How it was checked:** about 440 automated checks pass (`bin\DragFlow.Tests.exe`), including real picture, video, audio and text edits, an Excel workbook to PDF through Excel, folder rules with a real watch and a simulated restart, and the real dial driven by a fake mouse. 16 live checks with the real mouse pass (`tests\e2e-drag.ps1`); the Edit › Right check first failed because the new, longer column of options was closed the moment the pointer crossed a neighbouring slice — fixed, with a test. A third independent code review found 12 problems (the worst: an hourly rule scan redoing every conversion after a restart, "older than" counting copies as old, a Recycle Bin message that was wrong on USB sticks, Office conversions that could hang on a prompt); all are fixed with regression tests. Version 1.3 is installed.

| # | Item | Status |
|---|---|---|
| 5.1 | Picture tools | Done, under Pictures › Edit: crop (1:1, 16:9), watermark, grayscale, border, collage, animated GIF (the GIF needs ffmpeg). All undoable. |
| 5.2 | Video tools | Done, under Videos › Tools: compress to a size in MB (two passes), rotate, speed (0.25–4×, pitch kept), merge, contact sheet, subtitles from a matching `.srt` (needs an ffmpeg with libass). Silent videos and unusual sound codecs handled. |
| 5.3 | Audio tools | Done, under Audio › Tools: fade, merge, speed, trim silence. |
| 5.4 | Text tools | Done, under Text › Tools: line endings, UTF-8, tidy JSON, sort and de-duplicate lines, Markdown to HTML. Markdown to a rendered PDF goes through Edge like other web content. Edits in place keep a backup for Undo. |
| 5.5 | Office documents to PDF | Done: Excel and PowerPoint through Office (invisible, alerts off, a stuck instance is ended), or LibreOffice with its own profile. Tested with Excel on this PC. |
| 5.6 | Everyday extras | Done, under More: Compare (exactly two files), List contents (a CSV next to the folder, or in Documents for a whole drive), Recycle Bin (with a confirmation on the dial; put-aside items are reported honestly). |
| 5.7 | Smarter rules | Done: "older than N days" (by the newer of created/modified), "bigger than N MB", an hourly scan of existing files, Run now in Settings › Rules. Handled files are remembered on disk across restarts; paused rules stay paused through scans; the scan runs outside the watcher lock and skips cloud placeholders. |
| 5.8 | The dial from anywhere | Done: Send to › DragFlow (installed by the setup, removed by the uninstaller), clipboard files when no Explorer window is in front, type-to-jump on the shortcut dial. |
| 5.9 | Settings anywhere | Done: Settings › Backup — keep in another folder (a pointer file in the default folder), back up to a zip, restore from one; a restart keeps the profile options. A folder missing at start is waited for 10 s, then reported. |

**Still not done (same as before):** code signing (needs a paid certificate), a winget listing, actual translations, Windows notification centre copies, moving to the .NET SDK. Windows 11's short right-click menu still needs *Show more options*; *Send to* is the quicker route.

---

## Status: conversions, Tier 3 and Tier 4 built (version 1.2, 23 October 2026)

**How it was checked:** 350 automated checks pass (`bin\DragFlow.Tests.exe`), including real conversions in every direction, a real folder watch and the installer's contents, with both the Windows compiler and the modern one. 16 live checks with the real mouse pass (`tests\e2e-drag.ps1`), including the keyboard-shortcut dial. A second independent code review found 12 problems (the worst: a rule could loop on its own results, a recipe could recycle files it had only moved, updates made by the release script could not be read); all are fixed with tests. One test run out of six had 2 failures that did not come back in three further runs. Version 1.2 is installed with the new installer. The user guide is [README.md](README.md).

**Converting in every direction (asked for on 23 October)**

| Area | Status |
|---|---|
| Pictures | Done. JPG, PNG, WebP, AVIF, HEIC, GIF, TIFF, BMP and ICO, both ways. WebP, HEIC and AVIF pictures lead with "To JPG". Convert › Options… sets quality, size, a file-size target and where to save. |
| Video and audio | Done. MP4, WebM, MOV, MKV, GIF and sound-only; MP3, M4A, WAV, FLAC, OGG, Opus; resolution, quality, bitrate, remove sound. Needs ffmpeg, as before. |
| Web pages and text | Done. Saved pages, `.url` shortcuts and text files to PDF or to one long JPG or PNG, through Microsoft Edge running invisibly with an empty profile. |
| PDF pages | Done. To PNG or JPG. |

**Tier 3 – polish**

| # | Item | Status |
|---|---|---|
| 3.1 | Settings window | Done: 11 tabs, including Look, Shortcuts & updates, Actions, Custom actions (a table with a Test button), Rules and Recipes. The settings are still stored as small text files; nobody needs to edit them by hand any more, so no JSON switch was needed. |
| 3.2 | Onboarding | Done: a welcome window with a practice folder, and a tip on the first five dials. |
| 3.3 | Notifications and tray | Done: DragFlow's own notification cards with Undo, Show and Details buttons; the tray icon shows on, paused or off. Not done: copies in Windows' notification centre (that needs an app identity only packaged apps get easily). |
| 3.4 | Start-up | Done: starts a few seconds after sign-in at low priority; on by default from the installer and welcome window. |
| 3.5 | Installer, signing, updates | Installer done (`DragFlow-Setup.exe`, per user, listed in Windows' installed apps with an uninstaller). Updates done, from a folder or web address you choose, checksum-verified. Signing is built into `scripts\release.ps1` but **not done**: it needs a code-signing certificate, which costs money. Not done: a winget listing (it needs a public download address). |
| 3.6 | Accessibility | Done: number keys pick a slice mid-drag; the shortcut dial works entirely by keyboard; high contrast; animations off; a larger dial; screen readers are told the focused action. Not yet tried with Narrator by a person. |
| 3.7 | Performance | Done: the unchanging ring is drawn once, frames reuse one picture; a frame takes about 4–5 ms at 150 %. Nothing runs while you are not dragging, except a once-a-second health check. |
| 3.8 | Modern build | Done as an option: `scripts\get-compiler.ps1` fetches Microsoft's current compiler (signature checked) and the build uses it; a GitHub Actions workflow is ready for when the project is on GitHub. The code stays compatible with the compiler built into Windows. Not done: moving to the .NET SDK, which is not installed on this PC. |
| 3.9 | Docs and privacy | Done: README, PRIVACY, CHANGELOG. |
| 3.10 | Translation | Done as a system: a translation file per language and a template with 306 texts (1.3). Not done: actual translations, and some result messages from file operations ("3 images converted") are still English-only. |

**Tier 4 – power features**

| # | Item | Status |
|---|---|---|
| 4.1 | Dial without dragging | Done: Ctrl+Alt+D (changeable) opens the dial for the selection; click or use arrows, numbers, Enter and Esc. |
| 4.2 | Right-click entry | Done: "DragFlow actions…" for files and folders. On Windows 11 it is under "Show more options", because the new short menu only takes packaged apps. |
| 4.3 | Folder rules | Done: wait for downloads to finish, ignore half-downloads and DragFlow's own results, pause a rule that fires more than 30 times a minute, one Undo per batch. Tested with a real folder watch. |
| 4.4 | Recipes | Done: built in Settings › Recipes, shown on the dial under Custom; one Undo reverses the whole recipe; in-between files are tidied away. |
| 4.5 | Activity window | Done: searchable list with Undo and Show (tray › Activity…). |

---

## Status: Tier 1 and Tier 2 built (version 1.1, 22 October 2026)

Every Tier 1 and Tier 2 item below is built, except where the table says otherwise. Part 1 still describes version 1.0 as it was reviewed; the "known bugs" list there is what this work fixed.

**How it was checked**

- `bin\DragFlow.Tests.exe`: 237 automated checks, all passing (73 before). The tests run in a throwaway folder, so they never touch your settings, undo history or Recycle Bin. New: the whole drag path with a fake mouse and dial, the undo journal, cancel and partial failure, path guards, learning, ranking, profiles, Move-to, mixed selections, new actions, rename, shelf, drops from other apps, option layout.
- `tests\e2e-drag.ps1`: 14 checks with the real mouse against a real Explorer window, all passing: dial opens, To PDF and Rotate > Right work, the PDF is in the undo journal, flick and rubber-band show nothing, Esc closes the dial, dropping on the shelf parks a file and dragging it back out drops it into a folder, the log has no errors. One run out of five stopped early on a timing hiccup in the script itself; the rerun passed.
- An independent code review found 13 problems. The most serious: a failed undo could delete its own backup, housekeeping could remove the backup of a job still running, very large items could skip the Recycle Bin, and Extract here could record an unrelated download for undo. All 13 are fixed, with tests where they can be tested.
- Version control: the folder is now a Git repository with a commit per milestone.

**Tier 1 – reliability and safety**

| # | Item | Status |
|---|---|---|
| 1.1 | Self-healing hook | Done. The hook has its own thread that only pumps messages; a second thread turns events into drags. A once-a-second check re-installs the hook if the pointer moves but no events arrive, and after unlock or sleep. |
| 1.2 | No stuck drags | Done. If the button is found released without a button-up, the drag ends. A new press ends a drag left open. The dial closes after a minute at most, and on lock, display change, pause or turning off. |
| 1.3 | Snappy pointer and dial | Done. The menu is built on the background thread. ffmpeg, OCR and Word checks are cached; custom actions and profiles are re-read only when their file changes; the "folder has images?" check looks at 300 entries at most. |
| 1.4 | Undo for every action | Done. A journal on disk keeps the last 20 steps for 7 days. Files DragFlow made go to the Recycle Bin on undo (drives without one get a "DragFlow removed" folder). Moves go back, edits are restored from a backup, removed empty folders come back, wallpaper goes back. Every notification has an Undo button, and the tray menu lists the history. Custom actions run other programs and cannot be undone. |
| 1.5 | Excluded apps and quiet mode | Done. Excluded programs, excluded folders, quiet while a full-screen app or presentation is on any screen, and "Pause for 1 hour" in the tray. |
| 1.6 | Elevation | Done. Drags from administrator windows keep the dial hidden, with a one-time explanation. DragFlow restarts itself at normal level if started as administrator, and the installer starts it that way. A second launch opens Settings. |
| 1.7 | Explorer tabs and desktop | Done. The selection comes from the tab under the pointer, and the desktop's selection is asked of the desktop itself instead of matching names. |
| 1.8 | Cancellable long jobs | Done. Jobs over 1.5 s show a progress window with Cancel; what finished can be undone. More than 2,000 files asks first. Duplicates skips online-only OneDrive files. Windows, Program Files, drive roots and the user folder are refused. One bad file no longer stops a batch; the rest are listed under "Details". |
| 1.9 | Crash safety and diagnostics | Done. Errors are logged and DragFlow keeps running; after a fatal crash it restarts (at most once a minute). An always-on rolling log in `%LOCALAPPDATA%\DragFlow\logs` leaves file names out unless allowed. "Report a problem" saves a zip of the logs on the desktop. |
| 1.10 | Automated drag-path tests | Done locally (see above). Not done: running them on GitHub Actions, because the project is not on GitHub. |
| 1.11 | Folders to another drive | Done. The folder is copied, checked, then the original removed; undo does the reverse. |

**Tier 2 – smart features**

| # | Item | Status |
|---|---|---|
| 2.1 | Learning intent detection | Done. Each drag's outcome adjusts, within safe limits, how long the dial watches before opening and how far you can move before it hides. Directions you always pass it by on brisk drags are learned. Settings > Smart explains what it learned, with Show it more / Show it less / Reset. |
| 2.2 | Context-aware ranking | Done. The action you use most for that kind of file (from that folder counts double) moves to 12 o'clock, and the option you use most comes first inside a slice. One use is not enough; habits fade with a two-week half-life. Pinned actions always come first. Not done: lifting an option out of its slice onto the ring, because the busiest dials are already full. |
| 2.3 | Profiles | Done, per folder and per program: actions first, actions hidden, Move-to folders. Edited in Settings > Profiles. |
| 2.4 | Multi-file and mixed | Done. Mixed selections get Combine to PDF, Zip, Move to, Rename, New folder, Convert and Share. The dial centre shows the count and size. Batches report failures per item. |
| 2.5 | Quick actions | Done: Copy here, Copy to (Ctrl over Move to), Choose folder, Open, recent apps, Open with…, Share… (Windows Share window, which includes Phone Link and nearby sharing), Email, Print, Properties, Desktop shortcut, New folder with selection, Combine PDFs, WebP/HEIC/AVIF input, Trim video/audio, Even volume. Not done: Split PDF, because Windows has no built-in way to write real PDF pages. For the same reason, Combine turns PDF pages into pictures (their text can no longer be selected); the Done notification says so. |
| 2.6 | Drag shelf | Done. While files are dragged, a "Drop here to park" zone opens at the right screen edge. Parked items fold into a slim tab, survive restarts, and drag out into any folder or app. Nothing is moved by parking. |
| 2.7 | Smart Move-to | Done: shelf, profile and favourite folders, folders open in other Explorer windows and tabs, recent targets, the usual folders, then "Choose…". Never offers the folder the items are in. Ctrl copies instead. |
| 2.8 | Drags from other apps | Done, opt-in per program in Settings > Apps & shelf. Attachments and pictures are saved to `Downloads\DragFlow` first; undo removes them again. Checked with synthetic drops in the tests; not yet tried live against Outlook or a browser. |
| 2.9 | Rename smarts | Done. Suggested patterns (photo date, shared name start, folder name), clean-up and case styles, a preview of every new name with clashes in red. |

**Known limits and what to try by hand**

- The Share window, e-mail, Print and Properties open Windows' own windows, so they are not in the automated tests. Try each once.
- Drags out of Outlook or Chrome need the program added in Settings > Apps & shelf first.
- Tier 3 (installer with signing, auto-update, onboarding beyond a first-run tip, accessibility) is not started. The tray now has a real version number (1.1.0) and the settings window replaced most of the INI editing.

---

## Part 1 – What DragFlow did before this work (version 1.0, in plain words)

DragFlow is a small Windows tray app. When you start dragging a file or folder in File Explorer (or on the desktop), a round "dial" fades in over the item you grabbed. Each slice of the dial is an action – *To PDF*, *Zip*, *Resize*, *Move to*, and so on – chosen to fit what you are dragging. Point into a slice (or into one of the little option chips that fan out beside it) and let go: the action runs on the files. If you just drag the file somewhere as usual, the dial gets out of the way and Windows behaves exactly as it always did.

### How it works, step by step

1. **Listening for the mouse.** At start-up DragFlow installs a *low-level mouse hook* – a Windows mechanism that lets one program see every mouse event system-wide. Each event is handled in a few microseconds: it only records state and raises events; nothing slow happens inside the hook.

2. **Spotting a drag that starts in Explorer.** On left-button-down it looks at which top-level window is under the pointer. Only three window classes count as "file surfaces": `CabinetWClass` (an Explorer window), `Progman` and `WorkerW` (the desktop). Holding **Alt** when you press tells DragFlow to stay out of it. When the pointer then moves further than Windows' own drag threshold (usually 4 px), a drag has begun.

3. **Working out what is being dragged.** A dedicated background thread (so the pointer never stutters) does two checks:
   - *Is there actually a file item under the press point?* It asks UI Automation for the element at that point and walks up a few parents looking for a list item, data item or tree item. Nothing found means you started a rubber-band selection on empty space, so the dial must never appear.
   - *Which items are selected?* For an Explorer window it talks to Explorer through its COM automation interface (`Shell.Application`) and reads the selected items' full paths. On the desktop it reads the selected item names and matches them against the files in your Desktop folders.

   This guess is only used to decide which actions to offer. The real file list always comes from the drop itself (step 6), so a wrong guess can never make the app touch the wrong files.

4. **Deciding whether to show the dial (the "intent engine").** This is pure logic driven by the mouse path, so it is fully covered by tests. It moves through three states:
   - **Observing** – for the first 60 ms after the drag starts it just watches. *Speed* is the length of the path over the last 100 ms, in pixels per second.
   - **Revealing** – after the observe window, if the pointer is still inside where the dial would be (within 92 px of the press point) *and* it is not about to leave it within the next 80 ms at the current speed (distance moved + speed × 0.08 s must stay under 140 px × your sensitivity setting), the dial opens, anchored on the item you grabbed. A quick flick in a straight line therefore never flashes the dial.
   - **Dismissed** – once the pointer travels further than 140 px from the dial centre (250 px while an options sub-menu is open), or you press **Esc**, the dial fades and stays hidden for the rest of that drag. While the pointer is over the dial or its chips it never dismisses.

   The engine also predicts which slice you are heading for from the last 120 ms of travel and lights it faintly before you arrive. The controller waits for the "is it a file?" answer before actually opening the dial; if that answer has not arrived within 1.5 s it assumes yes. Sensitivity (0.5 – 2.0) is the one tunable in Settings.

5. **The dial itself.** A layered, per-pixel-transparent, always-on-top window that never takes focus and is also an OLE drop target. Transparent pixels (the centre hole and everything outside the ring) let drops fall straight through to Explorer. It is drawn with GDI+ each frame (ring, hover and heading washes, icons from Segoe Fluent Icons, a label pill, up to six option chips), eases its animations at about 66 fps, follows the Windows light/dark setting, and scales per monitor DPI. Hovering a slice that has options for 220 ms fans the chips out beside it, positioned so they never overlap the ring or each other (tested).

6. **The drop.** Explorer's own drag delivers the files to the dial as a normal file drop. The dial answers "Copy" so Explorer never deletes the originals, and only accepts a drop on a leaf action (not on a slice that merely opens chips). The drop returns to Explorer immediately; the action is queued on a fresh message so Explorer is never blocked.

7. **Running the action.** Things that need a dialog, the clipboard or to launch a program (rename, copy path/file/image, open terminal, custom actions) run on the UI thread. Everything else runs on a background task. If it takes more than 1.5 s you get a "Working…" balloon; at the end a "Done" balloon, and clicking it reveals the output in Explorer. Errors are shown as balloons too.

### What the dial offers

| You drag… | Slices (sub-options in brackets) |
|---|---|
| Images | To PDF · Convert (PNG/JPG/BMP/GIF) · Resize (Shrink, 50 %, 1920 px, 1280 px) · Rotate (Left/Right/Flip) · Copy text (Windows OCR) · Move to · Rename · More (Copy image, Wallpaper, Strip EXIF, Zip, Copy file, Copy path) |
| Folders | Rename (folder / contents) · Zip · Images to PDF · Convert · Organize (By type, By date, Flatten, Remove empty) · Duplicates · Move to · More (Copy path, Terminal, Size report) |
| Archives (zip, 7z, rar, tar.gz…) | Extract here · Extract to folder · Rename · Move to · More (Copy file, Copy path, SHA-256) |
| PDFs | To images · Copy text (OCR) · Rename · Zip · Move to · More |
| Video / audio (needs ffmpeg) | Convert (MP4/GIF/MP3/WAV, or MP3/WAV/OGG) · Shrink · Thumbnail · Rename · Move to · More |
| Word documents (needs Word) | To PDF · Rename · Zip · Move to · More |
| Text / code files | Copy text · Count · Rename · Zip · Move to · More |
| Anything else or mixed | Rename · Zip · Move to · More |

Plus a **Custom** slice for your own commands from `%APPDATA%\DragFlow\actions.ini` (one line per action: label, file types, program, arguments with `{file}`, `{files}`, `{dir}`), re-read on every drag. *Move to* lists your favourite folders from Settings first, then Desktop, Documents, Downloads, Pictures.

### Undo

The tray menu has **Undo last rename / move**. Under the hood every undoable operation is recorded as a list of moves (undo = move each item back, skipping any whose original name is now taken) or restores (rotate/flip keep a backup copy in `%TEMP%` and undo copies it back). It is a last-in-first-out stack kept in memory.

Covered: rename, batch rename, Move to, Organize by type/date, Flatten, Duplicates, Rotate/Flip. **Not covered:** Zip, Convert, Resize, Shrink, Strip EXIF, To PDF, PDF to images, Extract, Remove empty folders, Wallpaper, custom actions. The stack is lost when DragFlow exits, and the rotate backups are never cleaned up.

### Settings and configuration

- `%APPDATA%\DragFlow\settings.ini`: Enabled, Sensitivity, Favorites. Edited through a small WinForms dialog (tray → Settings…), which also toggles *Start with Windows* (a `Run` registry entry).
- `%APPDATA%\DragFlow\actions.ini`: custom actions, edited in Notepad.
- Diagnostics: only when the environment variable `DRAGFLOW_LOG=1` is set; writes `%TEMP%\dragflow.log`.
- Single instance: a second launch silently exits.

### Under the hood (the technical stack)

- **Language/runtime:** C# 5 on .NET Framework 4.x, compiled with the `csc.exe` that ships inside Windows – no SDK needed. That is clever for zero-setup builds but it caps the language at 2012-era features (no `async/await`, no modern syntax) and ties the app to .NET Framework.
- **UI:** WinForms for the tray icon, settings and prompts; raw Win32 for the hook and the layered dial window; GDI+ for drawing.
- **Windows services used:** UI Automation and Explorer's Shell COM for the selection; Windows' built-in OCR (`Windows.Media.Ocr`) and PDF renderer (`Windows.Data.Pdf`); `System.IO.Compression` for zip and Windows' own `tar.exe` for 7z/rar/tar; `ffmpeg` if found next to the exe or on PATH; Microsoft Word via COM if installed. A tiny hand-written PDF writer makes image PDFs with no library.
- **Files:** 24 source files (~3,400 lines), each under 300 lines, with file operations kept UI-free so they are testable. Manifest: runs as the invoking user, per-monitor DPI aware v2.
- **Build:** `scripts\build.cmd` → `bin\DragFlow.exe` and `bin\DragFlow.Tests.exe`. **Install:** `scripts\install.ps1` copies the exe to `%LOCALAPPDATA%\DragFlow`, adds a Start Menu shortcut and starts it (`-Uninstall` removes).
- **Version/signing:** no version number (shows 0.0.0.0), no icon file (the tray icon is drawn at runtime), unsigned – SmartScreen will warn anyone else who installs it.

### What the tests cover

`DragFlow.Tests.exe` is a dependency-free runner with **73 checks, all passing** on this PC (22 Oct 2026, including the OCR and ffmpeg ones):

- Intent engine driven by synthetic mouse paths: flicks never show the dial, ordinary starts do, moving past hides it, pauses and hovers show it, heading prediction, dismissal is sticky.
- Dial geometry: slice hit-testing, see-through centre, chip layout never overlapping.
- File operations: PDF, convert, shrink, resize, rotate, zip, extract (including refusing archives that try to escape their folder), tar.gz, SHA-256, batch rename, Move to, sort by type/date, flatten, duplicates, remove empty, size report, text stats, OCR of an image and of a PDF, PDF to images, video thumbnail – and that undo reverses each undoable one.
- Custom actions parsing and matching; menu composition for each file kind, slice/chip limits, fallbacks.

`tests\e2e-drag.ps1` drives the real mouse against a real Explorer window: a normal drag must show the dial, dropping on *To PDF* must create the PDF and leave the original, Rotate → Right must rotate, a flick and a rubber-band selection must show nothing. It takes screenshots into `bin\`.

**Not covered by automated tests:** the mouse hook, the controller's timing logic (hook → engine → dial), the dial window, the action runner, settings and the tray. These only get exercised by the manual end-to-end script.

### Known bugs and rough edges (found by reading the code)

Ordered by how much they matter.

1. **The hook can silently die.** Windows removes a low-level mouse hook whose callback is slow a few times (the default budget is a few hundred ms). Today the callback does window look-ups and *writes to the log file* inline, and it runs on the UI thread – so any busy moment on the UI thread (rendering a big dial at 200 % DPI, building the menu, a modal dialog) can get the hook dropped. DragFlow has no way to notice or re-install it: it just stops reacting until restarted.
2. **A missed button-up leaves the dial stuck.** If the button-up never reaches the hook (a UAC prompt, the secure desktop, a sleep/lock, Explorer cancelling the drag) the dial stays on screen until the next click. There is no watchdog that checks whether the button is really still down.
3. **Menu building runs on the UI thread** (`ActionCatalog.For`): it lists the dragged folder's files to see if it contains images, scans PATH for ffmpeg, checks favourite folders exist and re-reads `actions.ini` – every drag. For a big folder or a network drive this stalls both the dial and the mouse.
4. **Undo is partial and volatile** (see above): most actions cannot be undone, the history is lost on exit, there is only one level, and the "Done" balloon offers no Undo button.
5. **Windows 11 Explorer tabs:** the selection look-up matches the window handle, which all tabs share, so with several tabs it may read another tab's selection and offer the wrong slices (the files acted on are still the right ones).
6. **Desktop drags guess by name.** Items on the desktop are matched by file name, so two items with the same stem, hidden extensions, or shortcuts can mis-identify.
7. **No way to exclude anything:** the dial appears for every drag from Explorer, including when a full-screen game or a presentation is running on another monitor.
8. **Elevation:** if Explorer (or another file manager) runs as administrator, its drops cannot reach DragFlow's window (Windows' UIPI rule), so the dial opens but nothing happens on drop. There is no detection or message.
9. **Long jobs cannot be cancelled** (ffmpeg, duplicates over a huge tree, OCR of a 50-page PDF) and give no progress – only a "Working…" balloon.
10. **OneDrive files-on-demand:** *Duplicates* and *Size report* read every file, which makes OneDrive download placeholders.
11. **Crash = silent death.** An unhandled exception on the UI thread ends the app with no message and no restart.
12. **Moving folders to another drive** is refused with a message instead of being done.
13. **No version number, no code signing, no installer, no update check, no icon file.**
14. **Accessibility:** the dial is mouse-only; no keyboard, no screen-reader support, no reduce-motion or high-contrast handling.
15. **Second launch does nothing** (no "already running" hint, does not open Settings).
16. **Rotate backups in `%TEMP%\DragFlow\undo` are never deleted.**

Things that are genuinely good and should be kept as they are: the intent engine design and its tests; the "the drop carries the truth" principle; "Copy" effect so originals are never lost; zip-slip protection; never overwriting (always " (1)" names); file operations kept free of UI; per-monitor DPI handling; the compact, readable code.

---

## Part 2 – How it compares with similar tools

| | **DragFlow today** | **Windows built-in drag & drop** | **DropIt** (Windows) | **Dropzone 4** (macOS) | **Yoink** (macOS/iOS) | **Drag-shelf utilities for Windows** (DropPoint, Shelf-style apps) | **PowerToys** (context-menu tools) |
|---|---|---|---|---|---|---|---|
| How it appears | Dial over the item the moment you drag | Nothing; modifier keys change move/copy/link | A floating target icon you drop onto | A grid of targets slides out from the menu bar when a drag starts | A shelf slides in at the screen edge when a drag starts | A shelf/panel to park files in | Right-click menu entries |
| Zero-click discovery of actions mid-drag | **Yes** | No | No (icon only) | Yes | Yes (shelf) | Partly | No |
| Flick/intent detection so it stays out of the way | **Yes** | – | – | No (always slides out) | No (always slides in) | No | – |
| Actions on files | ~50 built-in, file-type aware | Move/copy/link | Dozens via rules (move, copy, compress, extract, rename, open with, upload, encrypt, print…) | Folders, apps, share services, zip, image resize, FTP/S3, custom scripts | Few (it is a holding area) | Few | Image Resizer, PowerRename, File Locksmith, Peek |
| Rules / automation | Custom commands in an INI | – | **Strong** rule engine, folder monitoring | Action API (Ruby/Python) | – | – | – |
| Park files to drag later ("shelf") | No | No | No | Yes ("Drop Bar") | **Yes (its whole point)** | Yes | No |
| Learns from you | No | – | No | No | No | No | – |
| Undo | Partial | Ctrl+Z in Explorer | No | No | – | – | – |
| Settings UI | Minimal dialog + INI files | – | Full | Full | Full | Basic | Full |
| Installer / signed / updates | No | – | Yes | App Store | App Store | Mostly yes | Yes |

**Where DragFlow already leads:** nobody on Windows offers actions *at the moment of the drag* with a dial that knows when not to appear. Its breadth of built-in actions (OCR, PDF, ffmpeg, organise, duplicates) beats the Windows shelf apps and matches Dropzone.

**Where it is behind:** a shelf to park files (Yoink/Dropzone), a real rules engine and open-with/share integrations (DropIt/Dropzone), polish (settings UI, installer, signing, updates), and robustness guarantees that a tool with a global hook must have.

---

## Part 3 – Roadmap

Effort is a rough guess for one developer with Claude: **Small** = an hour to half a day, **Medium** = one to three days, **Large** = a week or more. Risk means "how likely this is to break what works today or to be hard to get right".

### Tier 1 – Reliability and safety (do first)

| # | Item | Why | Effort | Risk |
|---|---|---|---|---|
| 1.1 | **Self-healing mouse hook.** Run the hook on its own thread with its own message loop, keep the callback to pure bookkeeping (no logging, no window look-ups beyond what is needed), and add a watchdog that re-installs the hook if Windows drops it or if no events arrive while the mouse is clearly moving. | The number-one way a hook-based app "just stops working". Fixes bug 1. | Medium | Low |
| 1.2 | **No stuck drags.** Every tick, if the left button is no longer down (`GetAsyncKeyState`), end the session; hard cap of e.g. 30 s on a dial; close the dial on session lock, display change and when DragFlow is disabled. | Fixes bug 2; makes the dial trustworthy. | Small | Low |
| 1.3 | **Keep the pointer and dial snappy.** Build the menu on the resolver thread, cache ffmpeg/OCR/Word look-ups (refresh every few minutes), skip the "does this folder contain images" scan beyond the first 200 entries, and stop reading `actions.ini` unless its timestamp changed. | Fixes bug 3; no stalls on big or network folders. | Small | Low |
| 1.4 | **Undo for every action, kept on disk.** A journal in `%LOCALAPPDATA%\DragFlow\undo` of the last 20 operations. Operations that *create* files (zip, convert, PDF, extract…) undo by sending their outputs to the Recycle Bin; in-place edits keep their backups there and clean up after 7 days; moves work as today. Multi-level undo in the tray menu, plus an "Undo" button on the Done notification. Nothing is ever permanently deleted by DragFlow – everything goes through the Recycle Bin. | The safety promise that makes people comfortable dropping on a slice they have never tried. Fixes bugs 4 and 16. | Medium–Large | Medium |
| 1.5 | **Excluded apps and quiet mode.** Settings list of programs/window classes where the dial never shows; automatic quiet mode when a full-screen app (game, presentation, video) is in front; "Pause for 1 hour" in the tray. | Fixes bug 7; prevents the dial interrupting things that matter more. | Small | Low |
| 1.6 | **Elevation awareness.** Detect an elevated drag source (compare integrity levels) and show a one-time hint instead of a dial that cannot receive drops; never run DragFlow elevated (warn in the installer); second launch opens Settings instead of exiting silently. | Fixes bugs 8 and 15. | Small | Low |
| 1.7 | **Correct selection with Explorer tabs and on the desktop.** Pick the *active* tab's shell view; on the desktop resolve items through the Shell (desktop folder view) instead of name matching. | Fixes bugs 5 and 6. | Small–Medium | Low |
| 1.8 | **Safe, cancellable long jobs.** A small progress window with Cancel for any job over a few seconds (ffmpeg, duplicates, OCR, big zips); confirmation before scanning more than ~2,000 files; skip OneDrive placeholders (or ask) in Duplicates/Size report; refuse to operate inside Windows/Program Files. | Fixes bugs 9 and 10. | Medium | Low |
| 1.9 | **Crash safety and diagnostics.** Global exception handlers that log, show a short message and keep the app (and hook) alive; a small always-on rolling log in `%LOCALAPPDATA%\DragFlow\logs` (no file paths unless the user opts in); "Report a problem" that zips the logs. | Fixes bug 11; makes future bugs diagnosable. | Small | Low |
| 1.10 | **Automated tests for the whole drag path.** Fake clock + fake hook + fake drop so the hook → engine → dial → action chain is tested without a mouse; tests for the action runner and settings; run the suite automatically on every change (GitHub Actions on a Windows runner). | Everything above touches timing code that only the manual script checks today. | Medium | Low |
| 1.11 | **Move folders across drives** (copy then delete, with undo through the journal). | Fixes bug 12. | Small | Low |

### Tier 2 – Smart features (what would make it stand out)

| # | Item | Why | Effort | Risk |
|---|---|---|---|---|
| 2.1 | **Intent detection that learns.** Record, per user, how each drag ended (used the dial / dismissed / flicked / dropped elsewhere), the speeds and directions involved, and tune the observe window and dismiss radius automatically within safe bounds. Learn "drags towards the left pane are navigation" and stop showing for those. A Learning page shows what it learned, with *Show it more / less* buttons and *Reset*. | The dial should feel like it reads your mind, not nag. Keep it transparent so it never feels random. | Medium | Medium |
| 2.2 | **Context-aware action ranking.** Order slices (and pick the one at 12 o'clock) by what you actually use for that file kind and source folder, with recency and frequency; pinned favourites always first. E.g. screenshots in Downloads → *Shrink* first. | Cuts the time to the right slice; the most-used action is always straight up. | Medium | Low |
| 2.3 | **Per-app and per-folder profiles.** Rules such as "from `Pictures\Phone` → offer To PDF, Resize, Move to Albums" or "anything in `Downloads` → Move to, Extract, Open with"; profiles also set different Move-to targets. | Different folders have different jobs. This is what DropIt users love. | Medium | Low |
| 2.4 | **Better multi-file and folder handling.** Mixed selections get a smart merged menu (images + PDFs → *Combine into one PDF*); the dial centre shows a count and total size; batches over a threshold show progress (1.8); per-item error summary at the end instead of stopping at the first failure. | Real selections are messy; today mixed drags only get the generic menu. | Medium | Low |
| 2.5 | **More quick actions.** *Copy here* (duplicate), *Move/Copy to… (pick a folder)*, **Open with** (apps registered for the type), **Share** (Windows share sheet, email as attachment), *Combine PDFs* / *Split PDF*, *Convert HEIC/WebP/AVIF* (Windows codecs), *Trim video* / *Normalise audio* (ffmpeg), *New folder with selection*, *Create desktop shortcut*, *Print*, *Properties*, *Send to phone* (Phone Link). | Breadth is DragFlow's edge; these are the most-requested gaps versus Dropzone/DropIt. | Small–Medium each | Low |
| 2.6 | **A drag shelf ("Park").** A *Park* slice drops the items onto a slim floating shelf at the screen edge. Later you drag them out of the shelf into any app or folder (the shelf is a proper drag source), stack several groups, pin the shelf, clear it. | Closes the biggest gap with Yoink/Dropzone and solves "I need these in another window" on Windows. | Large | Medium |
| 2.7 | **Smart Move-to targets.** Recently used folders, the folders currently open in other Explorer windows/tabs, Quick Access pins, and a Ctrl toggle for Copy instead of Move. | Move-to is the most-used slice; its targets should be the ones you need right now. | Medium | Low |
| 2.8 | **Drags from other apps** (opt-in per app): Outlook attachments, browser images/links, Office. These provide "virtual files" rather than paths, so the dial has to save them first. | Extends the idea beyond Explorer; the same dial for "save this attachment as PDF". | Large | Medium |
| 2.9 | **Rename smarts.** Suggest patterns from EXIF dates, detect existing numbering, preview all names, "clean up names" (spaces, case). | Rename is the second most-used slice. | Small | Low |

### Tier 3 – Polish (what makes it feel finished)

| # | Item | Why | Effort | Risk |
|---|---|---|---|---|
| 3.1 | **A real Settings window** replacing the INI files: pages for General, Dial (size, sensitivity, theme, animation), Actions (enable/disable, order, pin), Custom actions (table editor with a *Test* button), Profiles, Excluded apps, Learning, About/Updates. JSON config with automatic migration from today's INI files. | The INI + Notepad approach blocks every other feature and scares non-technical users. | Large | Low |
| 3.2 | **Onboarding.** First-run walkthrough with a sample file to drag; tips in the dial for the first few uses; sample custom actions. | People will not discover a mid-drag dial unless shown once. | Small–Medium | Low |
| 3.3 | **Modern notifications and tray.** Windows toasts (with Undo / Open buttons) instead of balloon tips; a proper tray menu with status and *Pause*. | Balloon tips are deprecated and easy to miss. | Medium | Low |
| 3.4 | **Startup done right.** Startup entry visible in Task Manager/Settings, delayed start so it never slows boot, start enabled by default after install. | | Small | Low |
| 3.5 | **Installer, code signing, updates.** Inno Setup (or MSIX) installer, a `winget` manifest, a real version number and icon; Authenticode signing (an OV certificate costs roughly $100–400 a year; Azure Trusted Signing is about $10 a month) so SmartScreen stops warning; in-app "check for updates" that downloads a signed release from GitHub and swaps it in on next start. | Required for anyone other than you to install it without warnings, and for fixes to reach them. | Medium (+ a small yearly cost) | Low |
| 3.6 | **Accessibility.** Keyboard control once a drag has started (arrow keys or 1–8 pick a slice, Enter runs, Esc closes), a larger-dial option, high-contrast theme, reduce-motion, and UI Automation exposure so Narrator can read the slices. | Required for a product that claims to be best-in-class; also helps everyone on a laptop touchpad. | Medium | Low |
| 3.7 | **Performance and battery.** Cache the static ring bitmap and only redraw changing parts; measure CPU during a drag and guarantee zero wake-ups when idle (already close); optional Direct2D renderer later. | Keep the "invisible when not needed" promise on laptops. | Small | Low |
| 3.8 | **Modern build.** Move to the .NET SDK (the `dotnet` tool is already on this PC) with a normal project file: modern C#, `async/await`, proper WinRT bindings, single-file publish, trimmed size, GitHub Actions builds. Keep `build.cmd` working until the switch is done. | The 2012-era compiler blocks async code, modern libraries and CI. Best done early in Tier 1 so later work is easier. | Medium | Medium |
| 3.9 | **Docs and privacy.** README with GIFs, a one-page privacy note (nothing ever leaves the PC; learning data stays local and can be wiped), a changelog. | | Small | Low |
| 3.10 | **Localisation.** Strings in resource files; English first, then whatever you need. | | Medium | Low |

### Tier 4 – Power features (added 23 October 2026)

Tiers 1 to 3 make DragFlow reliable, smart and finished. Tier 4 takes it beyond what drag tools usually do: the same actions without dragging, and work that happens on its own.

| # | Item | Why | Effort | Risk |
|---|---|---|---|---|
| 4.1 | **Dial without dragging.** A keyboard shortcut (Ctrl+Alt+D by default) opens the dial at the pointer for whatever is selected in Explorer or on the desktop; click an action to run it. | Many people select first and act second; this also helps anyone who finds dragging hard. | Medium | Low |
| 4.2 | **"DragFlow actions" in Explorer's right-click menu.** Opens the same dial for the selected items. | The place people already look for file actions. Registered per user, removed on uninstall. | Small | Low |
| 4.3 | **Folder rules that run on their own.** "When a new PDF lands in Downloads, move it to Documents\PDFs." Rules wait until a download has finished, every run can be undone, and a notification says what happened. | What DropIt users love, but with undo and with DragFlow's actions. | Medium | Medium |
| 4.4 | **Recipes.** Chain actions into one: "Resize to 1920 px, strip EXIF, zip" appears on the dial as a single action. | Repeated multi-step jobs become one drop. | Medium | Low |
| 4.5 | **Activity window.** Everything DragFlow did, searchable, with Undo and Show for each entry. | Trust: you can always see and reverse what happened. | Small | Low |

### Tier 5 – The toolbox (added 23 October 2026)

More to do with what you drag, and the dial reachable from anywhere.

| # | Item | Why | Effort | Risk |
|---|---|---|---|---|
| 5.1 | **Picture tools.** Crop to square or 16:9, watermark text, grayscale, border, a collage of several pictures, an animated GIF from pictures. | The edits people do most before sending or posting pictures. | Medium | Low |
| 5.2 | **Video tools.** Compress to a target size in MB, rotate, change speed, merge clips, a contact sheet of frames, burn in subtitles from a matching .srt. | "Make it fit in a chat" and "join these clips" are everyday needs. | Medium | Low |
| 5.3 | **Audio tools.** Fade in and out, merge, change speed, trim silence. | The same for sound. | Small | Low |
| 5.4 | **Text tools.** Fix line endings, convert to UTF-8, tidy JSON, sort lines, remove duplicate lines, Markdown to HTML and to a properly rendered PDF. | Small fixes that otherwise need an editor. | Small | Low |
| 5.5 | **Office documents to PDF.** Excel and PowerPoint through Office, or LibreOffice when that is installed. | Word already worked; the other two were missing. | Small | Low |
| 5.6 | **Everyday extras.** Compare two files, list a folder's contents as a spreadsheet, send to the Recycle Bin. | Frequent asks that need no other program. | Small | Low |
| 5.7 | **Smarter rules.** Conditions "older than N days" and "bigger than N MB", an hourly scan of existing files, and a Run-now button. | "Tidy my Downloads every week" becomes a rule. | Medium | Medium |
| 5.8 | **The dial from anywhere.** From Explorer's *Send to* menu, for files on the clipboard (copied files) when no Explorer window is in front, and type a few letters to jump to an action. | The dial should never be out of reach. | Medium | Low |
| 5.9 | **Settings anywhere.** Keep settings in a folder you choose (such as OneDrive), back them up and restore them. | Same DragFlow on every PC. | Small | Low |

### Tier 6 – Beyond the dial (added 27 October 2026)

What no other drag tool does: understand the files, reach other devices, protect them, and let you extend it.

| # | Item | Why | Effort | Risk |
|---|---|---|---|---|
| 6.1 | **AI helper (opt-in, your own key).** Name a file by its content, summarise, describe pictures, translate, pull a table out of a picture or PDF into a spreadsheet, ask anything about a file, draft an e-mail about files. Off until you paste an Anthropic API key in Settings; the key is stored encrypted for your Windows account only; every AI action is marked "AI" on the dial because it sends that file. | The thing people would switch for. | Large | Medium (costs a few cents per use; files leave the PC only when you choose an AI action) |
| 6.2 | **Smart names without AI.** Photos named by the date they were taken, PDFs and Office files by their title, scanned invoices by the date and number found in them; photos numbered in the order they were taken. | Daily tidying, fully on the PC. | Medium | Low |
| 6.3 | **Similar photos.** Finds the same picture at another size, slightly cropped or re-saved, keeps the best one and moves the others aside (undoable). | The duplicates finder only found exact copies. | Medium | Low |
| 6.4 | **To phone.** Drop files on *To phone*; a QR code appears; scan it with the phone on the same Wi-Fi and download. No account, no cloud; the link stops working when the window closes or after 15 minutes. | Getting files to a phone is still fiddly. | Medium | Low (Windows asks once whether to allow it on the network) |
| 6.5 | **Share link.** Copies the file into OneDrive and opens OneDrive's own sharing window for it. | A link instead of an attachment. | Small | Low |
| 6.6 | **Protect.** Encrypt and decrypt files with a password (AES-256, checked against tampering), and check a download against a checksum you copied from its website. | Safe sending and safe downloading. | Medium | Low |
| 6.7 | **Scripts as actions.** Drop a PowerShell, Python or batch script into the scripts folder and it appears on the dial for the file types it declares. | The plug-in system power users ask for. | Small | Low |
| 6.8 | **Scheduled rules.** Rules that run daily or weekly at a set time, only for files downloaded from the internet, or that run a recipe. | "Every Sunday night, tidy Downloads." | Small | Low |
| 6.9 | **Record a recipe.** "Save what I just did as a recipe" from the tray. | Recipes without building them step by step. | Small | Low |
| 6.10 | **Preview in the dial.** A thumbnail of what is being dragged in the middle of the dial and its size, dimensions or length below it. | Know what you are about to act on. | Medium | Low |
| 6.11 | **Gestures.** Draw a quick shape while dragging (Z, V, L, N) and that shape's action appears next to the pointer; move onto it and let go. | Faster than the dial once you know your favourites. | Medium | Medium (a missed gesture must never get in the way) |
| 6.12 | **Pen and touch.** Pen and finger drags get a larger dial that waits a little longer. | Tablets and touch screens. | Small | Low |

### Tier 7 – The dashboard, and always running (added 27 October 2026)

One good-looking window to manage everything, and DragFlow that is simply always there.

**Always running.** DragFlow cannot be a true Windows *service*. Services run in a hidden, separate part of Windows (no screen, no mouse), so a service could neither see your drags nor show the dial. What production apps of this kind do instead (OneDrive and PowerToys among them) is run for each signed-in user, started by Windows and watched so it comes back:

| What happens | Today | After Tier 7 |
|---|---|---|
| PC starts and you sign in | Starts from the "Run" list, 6 seconds after sign-in | Starts from a **Windows scheduled task** at sign-in (as well as the Run list), so it still starts when the Run entry has been switched off by a tuning tool |
| DragFlow crashes | Restarts once; a second crash within a minute leaves it off | A tiny **guard** (DragFlow's own second process, under 1 MB of memory) restarts it, waiting 5 s, 30 s, then 2 min; after 5 crashes in 10 minutes it stops and shows why |
| Someone ends it in Task Manager | Stays off | The guard brings it back within 5 seconds |
| You choose **Exit** in the tray | Exits | Exits, and the guard leaves it off until the next sign-in (asks: "Turn off until I sign in again" or "Turn off for good") |
| Explorer restarts, sleep, lock screen, another user signs in | The mouse watcher re-installs itself (since 1.1) | Same, plus the dashboard shows "watching the mouse: OK" or what went wrong |
| Uninstall | Removes everything | Also removes the task and the guard |

**The dashboard** replaces today's Settings window (12 tabs in a row). It is a modern window with a sidebar, light and dark modes that follow Windows, rounded cards and a live preview of the dial. It is drawn with WPF, which comes with Windows (no extra download), and opens from the tray icon, the Start menu or Ctrl+Alt+D twice.

| # | Page | What it does |
|---|---|---|
| 7.1 | **Home** | DragFlow on, paused or off with one switch; health (mouse watcher, rules, updates, the guard); today's and this week's actions; recent activity with Undo; quick switches (shapes, preview, AI, rules) |
| 7.2 | **Dial** | A live dial on screen: pick a file type, drag slices into the order you want, hide or pin actions, choose the size, colours and animations, and see the result straight away |
| 7.3 | **AI & keys** | A *Connections* card per service. Anthropic: paste the key (stored encrypted), *Test connection*, the model, and **usage**: tokens and an estimated cost this month, with an optional monthly limit after which AI actions pause. Room for later connections, such as a real OneDrive link through a Microsoft sign-in |
| 7.4 | **Scripts studio** | All your scripts as cards (name, file types, language, on/off). A built-in editor with line numbers and colours for PowerShell, Python and batch; templates ("rename with a pattern", "upload with curl", "resize with ImageMagick"); **Test on a file** with the output shown below; choose where it appears on the dial. Safety: a new or changed script runs only after you approve it once (it is fingerprinted), so nothing put into the folder by something else runs by itself |
| 7.5 | **Rules** | Rules as sentences you build by clicking: "When **a PDF** lands in **Downloads** and it is **older than 30 days**, **every Sunday at 18:00**, **move it to Archive**". A *Try it* button shows which files it would take, without doing anything |
| 7.6 | **Recipes** | Steps as blocks you drag into order; *Record* from what you just did; *Try on a file* |
| 7.7 | **Shapes & shortcuts** | Draw a shape on a pad to test it; pick each shape's action; the keyboard shortcut; the right-click and Send to entries |
| 7.8 | **Apps & quiet** | Programs and folders where the dial stays hidden; programs whose drags also get the dial; full-screen behaviour; pause for an hour or until tomorrow |
| 7.9 | **Profiles** | Folder and program profiles as cards instead of a table |
| 7.10 | **Backup, sync & updates** | Settings folder (OneDrive for every PC), back up and restore, the update source, *Check now*, version and what changed |
| 7.11 | **Privacy & data** | What DragFlow keeps and where, each with *Clear* (learning, action history, activity, undo history, the AI key, the files rules remember) |
| 7.12 | **Help & diagnostics** | The log with a search box, *Report a problem* (a zip on the desktop), the guide, keyboard help, *Reset all settings* |
| 7.13 | **Search** | A box at the top that finds any setting by name ("shelf", "key", "dark") |

**Production-ready, all through:** every change saves at once with an *Undo* toast (no OK/Cancel). Everything works by keyboard and with a screen reader. Text is ready to translate. Windows settings for high contrast, text size and reduced motion are followed. Settings files are checked and repaired at start (a damaged file never stops DragFlow). The installer sets up the task and the guard. Test checks cover every page and the guard. A **code-signing certificate** is still the one thing money must buy: without it, Windows SmartScreen warns other people who download DragFlow.

| Effort | Risk |
|---|---|
| Large (about the size of Tiers 5 and 6 together; the dashboard is most of it) | Low for the guard and task; Medium for the dashboard (a new window technology in the app, so everything gets the same live testing as the dial) |

---

## Part 4 – Suggested order

1. **"Rock solid" release (Tier 1 core):** 1.1 hook self-heal, 1.2 no stuck drags, 1.3 snappy menu, 1.9 crash safety + logs, 1.10 drag-path tests, 1.6 and 1.7 correctness fixes. About a week. Ship this to yourself and use it daily.
2. **Safety:** 1.4 full undo through the Recycle Bin, 1.8 cancellable jobs, 1.5 excluded apps. Then 3.8 the modern build, because everything after this benefits from it.
3. **Smart:** 2.2 ranking, 2.7 smart Move-to, 2.5 the top missing actions (Open with, Share, Copy to…, Combine PDFs, HEIC/WebP), 2.1 learning with its Learning page, 2.3 profiles.
4. **Stand-out:** 2.6 the Park shelf, 2.4 mixed selections.
5. **Finish:** 3.1 Settings window (can start earlier in a basic form), 3.3 toasts, 3.2 onboarding, 3.6 accessibility, 3.5 installer + signing + updates, 3.9 docs.

## Part 5 – Decisions needed from you

- **Which tier to start with** (the recommendation is Tier 1 core, then safety).
- **Modern build:** OK to move to the .NET SDK project format once Tier 1 core is done? It changes how the app is built (`dotnet build` instead of `build.cmd`) but not how it behaves.
- **Code signing:** are you willing to pay a small yearly fee for a certificate later (needed for a warning-free install on other PCs)?
- **Version control:** the folder is not a Git repository yet. Turning it into one (and optionally a private GitHub repo) gives you history and a safety net before the bigger changes.
