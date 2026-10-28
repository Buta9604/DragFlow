# DragFlow and your privacy

**Nothing leaves your PC unless you ask for it.** DragFlow has no account, no analytics and no telemetry. It works only with the files you drag or select.

## What it keeps, and where

| What | Where | Why |
|---|---|---|
| Settings, custom actions, profiles, rules, recipes, the shelf | `%APPDATA%\DragFlow` | So your choices are remembered |
| What you used, on which kind of file, from which folder (last 3000) | `%APPDATA%\DragFlow\usage.tsv` | To put your favourite actions first |
| How your recent drags ended (no file names) | `%APPDATA%\DragFlow\learning.ini` | To learn when the dial should open |
| Undo history and backups of edited pictures (7 days, last 20 steps) | `%LOCALAPPDATA%\DragFlow\undo` | So everything can be undone |
| Activity list (last 1000 actions) | `%LOCALAPPDATA%\DragFlow\activity.tsv` | The Activity window |
| Diagnostic log (two files of at most 1 MB) | `%LOCALAPPDATA%\DragFlow\logs` | To find problems. **No file names** unless you turn that on in Settings |
| Your AI API key, if you add one | `%LOCALAPPDATA%\DragFlow\ai-key.bin`, encrypted so only your Windows account on this PC can read it | For the AI helper. Never in the settings file, backups or logs; *Forget key* removes it |
| Which files folder rules already handled, and when scheduled rules last ran | `%LOCALAPPDATA%\DragFlow
ules-done.txt`, `rules-schedule.txt` | So rules never redo a file |
| Your scripts | `%APPDATA%\DragFlow\scripts` | Scripts as actions |
| Fingerprints of the scripts you approved | `%LOCALAPPDATA%\DragFlow\scripts-approved.txt` | A new or changed script runs only after you approve it |
| AI use per month (tokens and estimated cost, no file names) | `%LOCALAPPDATA%\DragFlow\ai-usage.tsv` | The AI and keys page, and the monthly limit |
| What the guard last did | `%LOCALAPPDATA%\DragFlow\guard.txt`, `guard-stop.txt` | Keeping DragFlow running; Exit tells the guard to stop |
| A sign-in task named "DragFlow" | Windows Task Scheduler (your account only) | Starts DragFlow when you sign in; removed when you turn off Start with Windows or uninstall |

Settings › Smart has *Reset learning* and *Forget action history*. The tray's Undo menu and the Activity window can clear their lists. Uninstalling removes everything in `%LOCALAPPDATA%\DragFlow` and, if you say so, your settings.

## When something does go online

These only happen when you ask for them:

- **Saving a `.url` web shortcut** as PDF or picture opens that web page, using Microsoft Edge with a separate, empty profile. Your browsing data is not used.
- **Share…** and **Email** hand the files to the app you choose in Windows' window.
- **Updates** are only looked for if you set an update source, and only there.
- **AI actions** (only when you have turned the AI helper on and added your own API key, and only for the file you pick an AI action for): that file is sent over an encrypted connection to Anthropic's Claude to be read, and the answer comes back. Pictures are made smaller first. Anthropic's own privacy terms for API use apply; DragFlow keeps nothing extra. Every AI action is labelled "AI" on the dial.
- **To phone** serves the chosen files to your own network (not the internet) at a secret link, until you close its window or after 15 minutes. Windows may ask once whether DragFlow may use the network.
- **Share link** copies the file into your OneDrive folder, which OneDrive then uploads as it does with any file there.
- **Your scripts** run as you wrote them; what they do is up to them.

*Report a problem* saves a zip of the logs on your desktop; it is not sent anywhere.
