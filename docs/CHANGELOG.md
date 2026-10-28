# Changelog

## 1.5.0 — 27 October 2026

**The dashboard**
- A new window for everything, opened from the tray (Settings…, or double-click the icon): Home, General, Dial, AI and keys, Scripts studio, Rules, Recipes, Shapes and shortcuts, Apps and quiet, Profiles, Backup and updates, Privacy and data, Help.
- Light and dark like Windows, in your Windows accent colour; high contrast is followed.
- Every change saves at once, with Undo for the last one. A search box finds any setting.
- Home shows whether DragFlow is working (mouse watcher, keep running, rules, AI cost), your week, quick switches and recent activity with Undo.
- Dial: a live picture of the dial for each kind of file, the order of actions, what is shown, size and colours.
- AI and keys: save or forget the key, *Test connection*, choose the model, see tokens and an estimated cost per month, and set a monthly limit.
- Scripts studio: an editor with line numbers and colours, templates, *Test on a file* with the full output. New or changed scripts run only after you approve them.
- Rules read as sentences and have *Try it*, which lists the files a rule would take, without doing anything.
- Recipes: steps in order, *Record what I just did*, *Run on files…*.
- Privacy and data: what DragFlow keeps, each with *Clear*. Help: a searchable log, the problem report, *Reset all settings*.
- The previous settings window is still there (Help › Classic settings window).

**Always running**
- A Windows sign-in task starts DragFlow even if a tuning tool switches off its startup entry. No administrator rights are needed.
- A small guard brings DragFlow back within seconds if it crashes or is ended in Task Manager. It waits longer each time, and stops after five times in ten minutes.
- *Exit* in the tray asks whether DragFlow stays off until your next sign-in or for good. Uninstalling removes the task and the guard.

## 1.4.0 — 27 October 2026

**Smart, without AI (nothing leaves the PC)**
- *Smart name*: photos named by the date they were taken, PDFs and Office files by their title, scanned invoices and receipts by the date and number found on them.
- *Number by date* (photos 001, 002… in the order they were taken) and *Similar photos* (finds re-saved, resized or lightly edited copies, keeps the best, moves the others aside).
- A preview of what you are dragging in the middle of the dial, with its size, dimensions, page count or length.

**AI helper (opt-in, your own Anthropic API key)**
- AI name, AI summary, AI describe (pictures), AI translate, AI table (a table from a picture or PDF into a spreadsheet), Ask AI about a file, and an AI e-mail draft for the files. Off until you turn it on and add a key in Settings › AI & more; the key is stored encrypted for your Windows account.

**Send and protect**
- *To phone*: a QR code to download the files on a phone on the same Wi-Fi. No account, no cloud.
- *Share link*: a copy in OneDrive and Windows' Share window, which offers *Copy link*.
- *Encrypt…* and *Decrypt*: password-protected copies (AES-256, tamper-checked).
- *Checksum*: compares a download with a checksum copied from its web page or a checksum file next to it.

**Automation**
- Scripts as actions: PowerShell, Python or batch scripts in the scripts folder appear on the dial.
- Rules that run daily or weekly at a set time, only for files downloaded from the internet, or that run a recipe.
- *Save last actions as a recipe…* in the tray menu and Settings.

**Faster ways in**
- Shapes: draw Z, V, L or N while dragging and that action appears beside the pointer (choose the actions in Settings).
- Pen and touch drags get a larger dial that waits a little longer.

**Fixes**
- Edits to custom actions saved twice within the same moment were sometimes not noticed.

## 1.3.0 — 23 October 2026

**The toolbox**
- **Pictures › Edit:** crop to square or 16:9, watermark text, grayscale, border, a collage of several pictures, an animated GIF from pictures (alongside rotate).
- **Videos › Tools:** compress to a size in MB, rotate, change speed, merge clips, a contact sheet of 16 frames, burn in subtitles from a matching `.srt`.
- **Audio › Tools:** fade in and out, merge, change speed, trim silence.
- **Text › Tools:** Windows or Unix line endings, convert to UTF-8, tidy JSON, sort lines, remove duplicate lines, Markdown to HTML.
- **To PDF** now works for Excel and PowerPoint files too, through Office or LibreOffice.
- **More:** compare two files byte for byte, list a folder's contents as a spreadsheet, send to the Recycle Bin.

**Rules**
- Conditions "older than … days" and "bigger than … MB"; an hourly look at the files already in the folder; *Run selected rule on existing files now*.
- Files a rule has handled are remembered across restarts, so an hourly scan never redoes a conversion or copy. Cloud placeholders (OneDrive "Files On-Demand") are left alone.

**The dial from anywhere**
- Explorer's **Send to › DragFlow** menu, the clipboard (copy files, then press Ctrl+Alt+D with no Explorer window in front), and type the first letters of an action to jump to it.
- Open options stay open while the pointer crosses a neighbouring slice on its way to them.

**Settings anywhere**
- Settings › Backup: keep the settings in a folder you choose (such as OneDrive), back them up to a file, restore them.

**Fixes from the third code review**
- Recycle Bin: on a USB stick or network drive (no Recycle Bin) the notification now says the items were put in a "DragFlow removed" folder instead of claiming they were recycled, and rules never pick those up again.
- Office conversions can no longer hang on a password or repair prompt: alerts are off, protected files fail with a message, and a stuck Office process is ended after three minutes or when you cancel.
- Subtitles work from any user folder (including names with an apostrophe); videos with Vorbis, FLAC or PCM sound, and silent videos, go through rotate, speed and merge without errors.
- A settings folder that is not available at start (an unplugged drive, a share, OneDrive not ready) is waited for briefly and then reported, instead of being silently replaced.
- Restarting after a settings move or restore keeps the profile options and no longer shows "DragFlow updated".
- Absurdly nested JSON is refused instead of crashing; code spans in Markdown keep their stars and brackets; comparing files reads correctly on network drives; a whole drive's contents list goes to Documents.

## 1.2.0 — 23 October 2026

**Converting in every direction**
- Pictures to and from JPG, PNG, WebP, AVIF, HEIC, GIF, TIFF, BMP and ICO. WebP, HEIC and AVIF pictures lead with *To JPG*.
- *Convert › Options…*: quality, resizing, a "no bigger than … KB" target, where to save. Remembers your last choices.
- Video: MP4, WebM, MOV, MKV, GIF and sound-only, with resolution, quality and "remove the sound". Audio: MP3, M4A, WAV, FLAC, OGG, Opus with bitrate.
- Web pages, `.url` shortcuts and text files to PDF, or to one long JPG or PNG. PDF pages to PNG or JPG.

**Power features**
- The dial without dragging: **Ctrl+Alt+D** or right-click › **DragFlow actions**, with full keyboard control.
- **Rules** that run by themselves when new files land in a folder (they wait for downloads to finish, never loop, can be undone).
- **Recipes**: several actions as one, on the dial under Custom; one Undo reverses them all.
- **Activity** window: everything DragFlow did, searchable, with Undo and Show.

**Polish**
- An installer (DragFlow-Setup.exe), an entry in Windows' installed apps with a proper uninstaller, and a program icon.
- Updates from a folder or web address you choose, checked against a checksum before installing.
- A welcome window with a practice folder, and tips on the first few dials.
- Settings for colours, dial size, animations, language, hidden actions, custom actions (a table instead of a text file), rules and recipes.
- High contrast, reduced motion, number keys to pick a slice, screen-reader names for the dial.
- The tray icon shows whether DragFlow is on, paused or off. Starting with Windows waits a few seconds so signing in stays quick.
- Translations: every text can be translated with a simple text file.

## 1.1.0 — 22 October 2026

- Reliability: a self-healing mouse hook, no stuck dials, a crash-safe app with a rolling log.
- Undo for every action, kept for 7 days, never deleting anything for good.
- Cancellable long jobs, protected system folders, quiet mode and exclusions, correct selection with Explorer tabs and on the desktop.
- Smart: learning when the dial should open, your favourite actions first, folder and program profiles, smart Move-to, Open & share, mixed selections, the shelf, drops from other apps, smarter batch rename.

## 1.0.0

- The first dial: about 50 actions for pictures, folders, archives, PDFs, video, audio, Word and text files.
