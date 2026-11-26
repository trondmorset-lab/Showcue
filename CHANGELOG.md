# Changelog

*[Norsk](CHANGELOG.no.md)*

All user-visible changes, newest first. The version number is in the file `VERSION`.
Each version gets its own release on GitHub (`v0.5.0` etc.) with the Windows and Mac programs.
The full history of earlier versions is in the Norwegian changelog.

## 0.27.1 – 2026-11-26

### Fixed
- **Go to scene** (Windows and Mac): a *Go to* cue that only names a scene (no cue number) always answered "cue not found" and never switched scene. It now switches scene as intended.
- **Go to loops** (Windows and Mac): *Go to* cues that start each other without any pause (for example two cues pointing at each other) kept the program busy for ever. After 25 jumps in a row without a pause the chain is stopped, with a message in the status line and an error in the show log. Normal loops with sound or waiting in between are not affected.
- **Preload follows GO** (Windows and Mac): the preload planner now stays inside the scene of the GO cursor (auto-continue never leads into another scene), follows a *Go to* cue to its target in the right scene, and skips cues marked *Skip in rehearsal* while rehearsal mode is on. Turning rehearsal mode on or off updates the preload at once. This only affects which files are prepared in advance.
- **Web remote with scenes** (Windows and Mac): the cue list and the position (*3 / 12*) on the web page now show the active scene. Before, a cue number used in several scenes was highlighted in all of them.
- **Live input** (Windows and Mac): a driver that delivers NaN or infinite samples can no longer send them on to the mixer; they are replaced by silence.
- Clock triggers: cues without a trigger are no longer parsed on every 16 ms tick.

### Tests
- The stress test (`tools/stress`) has new cases for the features merged in 0.27.0: scenes combined with rehearsal mode, GO and preload; scene editing; show files with the new fields (old files, corrupted values, round trip); routing presets; clock triggers around midnight and clock jumps; settings with wrong types and extreme numbers; ASIO input planning and driver events; show log shutdown and disk errors; and the web server over real sockets (malformed and oversized requests, many and slow connections, wrong PIN lock-out, a failing command handler, stopping with open connections, restart on the same port).
- New unit tests on both platforms (`InteractionTests`).

## 0.27.0 – 2026-11-25

### New
- **Show log** (Windows and Mac, same behaviour): a log of what happened during the show, for reports and troubleshooting. Each line has the time of day (ISO 8601), the time since the program started, and what happened:
  cue started (number, name, type and how: GO, auto-follow, auto-continue, hotkey, OSC, MIDI, timecode, clock, group, button panel or manual), cue finished or stopped, pause and resume, stop all (panic), the GO cursor moved by the user,
  errors and warnings (missing file, audio device lost, the error shown on the cue), show opened and saved, and program start and exit.
- **File → Show log** (on the Mac also *Tools → Show log*) shows the events of the current run live, newest at the bottom, with a filter (all, errors and warnings only, cues, control, show and program).
  *Open an older log* shows a previous run. *Export CSV* and *Export text* save what is shown; the CSV is safe to open in a spreadsheet (cells starting with `=`, `+`, `-` or `@` are neutralised).
- One log file per run in the `Logs` folder next to `Autolagring` and `Sikkerhetskopier` (Windows `%AppData%\ShowCue\Logs`, Mac `~/Library/Application Support/ShowCue/Logs`). The 30 newest files are kept, and a file stops growing at 5 MB.
- **Write show log** (*Settings → Safety*) is **on** by default. Logging never slows down or stops the show: events are queued and written by a background thread, and a full queue drops the oldest events (counted and shown in the log window). Disk errors are reported once and never interrupt the show.

## 0.26.0 – 2026-11-25

### New
- **Web remote** (Windows and Mac, same behaviour): a small built-in web server lets a phone or tablet on the same network control the show from a browser.
  The page shows the next cue (number, name, type), what is playing with time left, and big buttons **GO**, **Stop all**, **Pause/Resume**, **Next** and **Previous**, plus a read-only cue list.
  The page uses the same controls as OSC, so the double-GO protection applies. Show mode does not block it (as with OSC), but nothing can be edited remotely.
- Settings: *Settings → Web* with an on/off switch, the port (default 8088) and the PIN. The address (e.g. `http://192.168.1.20:8088`) is shown there and in the status line while the server runs.
- **Security:** the web remote is **off** by default and needs a **PIN** (6–32 letters or digits; a random one is made when it is empty or easy to guess, such as `123456`).
  Everything except the login page needs the PIN (session cookie after login), failed attempts are rate-limited with a lockout, and the server has fixed routes only, no file access, no CORS,
  size/time/connection limits and plain HTTP/1.1 with closed connections. **The traffic is plain HTTP (not encrypted), so the PIN only protects against casual use on a trusted network.**
  The page has not been tried on real devices yet.

## 0.25.0 – 2026-11-25

### New
- **Rehearsal mode** (Windows and Mac, same behaviour): skip chosen cues during rehearsals without deleting or disabling them.
  - New cue field **Skip in rehearsal** (*Timing* tab; saved as `SkipInRehearsal`, off by default, so old shows open unchanged). Marked cues show a small *REH* marker in the cue list.
  - **Turn the mode on and off** with Ctrl+R (⌘R on Mac), *Setup → Rehearsal mode* or the *Rehearsal* button. While it is on, a bright bar at the top of the window and the window title say so.
    The mode is **never saved**: the program always starts with it off.
  - While the mode is on, **GO, auto-continue and auto-follow** move past marked cues and never start them. Clock and timecode triggers do not start them either.
    Starting a marked cue directly still works: by cue number (OSC, MIDI show control), hotkey, MIDI trigger, button panel, double-click and as a target of a group cue.
  - **Check the show** warns while rehearsal mode is on and notes how many cues are marked.

## 0.24.0 – 2026-11-25

### New
- **Preload next cue** (Windows and Mac, same behaviour; *Settings → Safety*, **off** by default until it has been tried on real equipment).
  When on, ShowCue prepares the cue the GO playhead is on, and the cues it starts automatically after it (at most three files at a time), in a background thread as soon as the playhead moves:
  audio files are opened and the first seconds are read ahead (Mac: decoded into the ring buffer), the audio track of a video is opened the same way (Mac also loads the video file's properties), and pictures are decoded.
  “Go to” cues are followed to their target and group cues prepare their enabled targets. Disabled cues are skipped, like GO does.
  Preparation never runs on the audio thread. The files are released when the playhead moves, on stop, when a show is opened and on exit. If preparation fails, or the file has changed, the cue starts exactly as before.
  Note: the gain in start latency has not been measured; please try it on real equipment before relying on it.

## 0.23.0 – 2026-11-25

### New
- **Routing presets** (Windows and Mac, same behaviour): the routing matrix per cue already existed; now you can send a cue to e.g. the main speakers or the monitors in one click, without editing the matrix by hand.
  The **Presets** button under the matrix has:
  - **Named presets saved in the show** (e.g. “Main” = outputs 1-2, “Monitor” = 3-4, “Main + Monitor” = 1-2, 3-4). A preset is a list of output pairs, optionally with a level in dB per pair (“3-4:-6”), so it fits files with any number of channels: mono goes to both outputs of each pair, stereo goes left/right, and multichannel files are summed alternately to left/right at −3 dB. Create, edit (including renaming) and delete from the menu.
  - **Apply to all selected cues:** choosing a preset sets it on every selected cue that has audio, and the status line says how many were changed and how many were skipped (no audio, or no output in the patch that the preset fits).
  - **Copy routing / Paste routing** between cues, and **Use this routing on all selected cues**. Channels and outputs a target cue does not have are left out.
  - **A routing summary above the matrix:** the name of the matching preset, “Default”, “Off”, or “Output 1-2, 5”.
- Showfile: the new optional field `RoutingPresets` (list of `Name` and `Pairs` with `Bus` and `GainDb`). Older files open as before; older versions ignore the field. A damaged field never stops a show from opening.
- Nothing in this feature runs in the audio thread: presets only change the routing of the cue (on the main thread), which playing cues pick up exactly as when the matrix is edited by hand. It does not change playback and has no setting to turn on.

## 0.22.0 – 2026-11-25

### New
- **Scenes (cue lists)** (Windows and Mac, same behaviour): a long show no longer has to be one long list. Cues belong to a *scene* (e.g. “Act 1”, “Act 2”), and each scene has its own numbering, its own GO playhead and its own GO order.
  Choose the active scene in the picker above the cue list (◀ ▶ step between scenes; **Ctrl+Page Up/Down** on Windows, **⌘⌥←/→** on Mac). The *Scenes* menu creates, renames, deletes and reorders scenes and moves the selected cues to another scene.
- **GO never jumps to another scene by itself.** At the end of a scene the playhead stops at “end of list”; auto-continue/auto-follow also stay inside the scene. Each scene remembers where its playhead was.
- **Numbers per scene:** the same cue number can be used in several scenes. *Renumber* renumbers the active scene and updates fade, stop, go to and group targets. Fade, stop, go to and group targets look in the scene of the cue first and then in all scenes; write `Act 2:5` to point at cue 5 in a specific scene.
- **Go to scene:** a *go to* cue has a new *Scene* field. With only a scene it switches scene (the playhead is where it last was); with a number it jumps to that cue in that scene. OSC: `/scene/{name}` (or `/scene "Act 2"`) switches scene. The existing OSC commands look in the active scene first.
- **Check the show** accepts the same number in different scenes, reports duplicates inside one scene and checks that the scenes used by *go to* cues exist.
- Show files: a new optional `Scene` field per cue and a `Scenes` list. Shows without scenes load as one scene, and older versions open new files as one list.

## 0.21.0 – 2026-11-24

### New
- **Clock-time trigger** (Windows and Mac, same behaviour): a cue can start by itself when the computer's clock passes a chosen time (hh:mm:ss, local time), e.g. “doors open 19:00:00”.
  The *Clock-time trigger* field is in the *Timing* tab, and the time is shown in the trigger column of the cue list (“At 19:00:00”).
  Each cue starts once when the time is passed. Cues are not started after sleep, on large jumps in the clock (more than 2 seconds between two checks) or when daylight saving time changes.
- **Clock triggers active** (*Settings → Safety*) turns the feature on and off. It is **off** by default, so nothing starts unexpectedly.
- **Check the show** warns when cues have a clock trigger but the setting is off.

## 0.20.0 – 2026-11-24

### Changed
- **A new, clearer main window** (Windows and Mac, same behaviour; no features removed, shortcuts unchanged):
  - **Cue list:** the row that is *playing now* is green with a progress bar and a new **Remaining** column (time left, “∞” when the length is unknown).
    The cue *GO will start* has a blue edge and ▶. Disabled cues are dimmed and are never counted as next.
  - **The panel for the selected cue** is split into tabs: *Basic*, *Audio/media*, *Timing* and *Advanced*. All fields are still there, just grouped.
    Only tabs with content for the cue type are shown (types without audio, pictures or video have no Audio/media tab). The tab is kept when you select another cue of a type that has it.
  - **Top bar:** a larger GO button with “Space”, and *Next cue* in larger text with its type and a blue edge.
  - **Active cues:** time left in large green text, and more room in the bottom panel.
## 0.19.1 – 2026-11-24

### Fixed
- **Windows: a cue that fails in the middle of a show** no longer causes a new error message every 16 milliseconds. The cue gets an error, is stopped and removed from the list, and the error is logged once; the other cues keep playing.
- **Mac: values in the show file are limited when loading**, as on Windows (level −60…+12 dB, times 0–86,400 s, ports 1–65,535, MIDI values, universe, channel count). A corrupt or hand-edited file can no longer produce huge or invalid values.
- **Check the show knows group cues** (Windows and Mac): an empty target list is an error, targets that do not exist are an error, disabled targets give a warning, and a group that matches no cue is an error.
- **Settings are limited when loading** on Windows and Mac (ports, MIDI channel, double-GO protection, delay, fade time for stop all, channel count, button panel columns, patch level and channels, and more).
- The channel count of an audio file is limited to 1024 on Windows, so a corrupt value cannot create a huge routing table.

## 0.19.0 – 2026-11-24

### New
- **Check the show** (*File → Check the show*; on Mac also under *Tools*): a pre-show check that lists errors, warnings and OK results.
  It checks that all media files exist and can be read, that fade, stop and “go to” cues point to a cue that exists,
  that required fields are filled in and times are valid, that the selected audio output, audio input and screens exist, and that no cue numbers are equal.
  The check does not change the show. The same rules and texts on Windows and Mac.

## 0.18.0 – 2026-11-24

### New
- **Windows: live inputs through ASIO (experimental).** Under *Settings → Audio* (ASIO) you can turn on *Use ASIO inputs for live input*
  and choose the first input and the number of inputs. The inputs are taken from the same ASIO session as the output, so an input cue can use
  e.g. a network audio interface in ASIO mode. The feature is **off by default** and has not been tried on real ASIO equipment yet;
  when it is off, live input (WASAPI) and the audio output work exactly as before.
  If the driver has no inputs, or cannot open them, audio keeps playing without inputs and the reason is shown in the audio status.
- Mac needs no change: CoreAudio provides live inputs without sharing the output.

## 0.17.0 – 2026-11-20

### New
- **Group cue** (❖ *Control → Group*): starts several other cues with one GO. Write the numbers of the cues to start, e.g. `2, 5, 8-10`
  (`8-10` includes every cue with a number from 8 to 10). Each cue in the group follows its own pre-wait and continue setting,
  and disabled cues are skipped. If a cue does not exist, the others still start and the group shows which one is missing.
  A group cannot start itself, and groups that start groups stop at eight levels.
  *Renumber* updates the numbers in groups, including both ends of a range.

## 0.16.1 – 2026-11-24

### Fixed
- **Damaged show files:** on Windows a single invalid value (text in a number field, an unknown choice) no longer makes the whole show unopenable; the field gets its default value and the rest of the cue is read as usual.
  Empty entries (“null”) in the cue list, file lists and routing are skipped on both platforms, and a show without a title or cues opens empty.
- **Cue without a valid type:** the Mac version no longer guesses “Stop” for a cue with a missing or unknown type (which could stop everything on GO); the file is rejected with a message, as on Windows.
- **OSC in (Windows):** a blob with an enormous length in a packet could give an invalid position and make the rest of the message get lost.
- **Light (Windows):** “NaN” and “Infinity” are no longer accepted as DMX levels (the Mac version already rejected them).
- **Network cue:** `\xHH` is only interpreted with two real hex digits, the same on Windows and Mac (Windows accepted spaces, Mac accepted signs).
- **Quitting (Windows):** one failure during clean-up no longer prevents the audio output from being released and the program from closing.

## 0.16.0 – 2026-11-04

### New
- **Slideshow:** a new cue type that shows images, PDF pages and videos one after another on one screen, like a presentation.
  Add files under *＋ Media → Slideshow* (Ctrl+Shift+B / ⇧⌘B) and sort them with ↑ / ↓.
  Images and PDF pages are shown for the chosen time (*Time per slide*), each page of a PDF becomes a slide of its own, and videos play until they finish.
  Slides crossfade, the slideshow can start again after the last slide (loop) and play in random order.
  Turn off *Advance automatically* to step through by hand.
- **Stepping through a slideshow:** ⏮ / ⏭ under *Active cues*, or OSC `/cue/{nr}/next` and `/cue/{nr}/previous`.
- **Screenshots in the manuals** (README for Windows and Mac, Norwegian and English), taken by the program itself.

### Fixed
- **Windows: drop-down lists** for audio device, screen and MIDI channel showed “[key, name]” instead of just the name when closed.
- **Windows: the file list** in playlists and slideshows was white in the dark theme; it now follows the theme.
- **The type column in the cue list** is wider, so “Slideshow”, “Playlist” and “Message” are no longer cut off (Mac: no longer split over two lines).

## 0.15.0 – 2026-11-03

### New
- **Screen per cue:** video, image, PDF and web page cues can be shown on a different screen than the default (or in a separate window).
  Choose under *Screen* in the panel on the right; *Default* uses the screen from *Settings → Video*.
  Crossfade (“Replace other images/videos”) only applies to cues on the same screen.

### Fixed
- **Mac: the program could not be closed after a video had played.** The output window kept the app running when the main window
  was closed, and questions on quit could end up behind a full-screen output window. Closing the main window now quits the program
  (with the usual questions), and the output windows are hidden while the questions are shown.
- **Windows: safer shutdown.** Each clean-up step runs on its own, all output windows are closed, the program exits when the main
  window is closed, and a safety net ends the process if an audio or video driver should hang.

## 0.14.0 – 2026-11-03

### New
- **Real-time scrubbing:** audio and video cues that are playing have a scrub bar under *Active cues*. Drag to scrub,
  or jump 5 seconds with ⏪ / ⏩. Picture and sound in video follow each other, and it also works while paused.
  Scrubbing back out of the automatic fade-out near the end brings the level back.
- **Media cues without a file:** *＋ Media* creates an empty audio, video, image, PDF or playlist cue straight away.
  Choose the file later with *Browse*, or drop a file on the cue. A cue without a file shows “No file selected” if it is run.

## 0.13.1 – 2026-11-03

### Fixed
- **The logo shows in the Dock and the taskbar:** the Mac app now has its icon as an asset catalog (as newer macOS prefers)
  and sets it itself at startup. The Windows icon has been rebuilt in the classic format for all sizes and is set on every window.
- **Audio playback could hang** (the program froze) with a loop shorter than one sample, e.g. with the start and end markers almost on top of each other.
- **Invalid numbers** (NaN/infinity) in times and levels gave wrong audio and made the show impossible to save; on the Mac the app could crash.
- **Corrupt OSC packets** from the network could cause errors (Windows) or a crash (Mac).
- Status out over OSC handles very long times; autosave and backups handle very long file names.
- “Normalise loudness” no longer sets −60 dB if the measurement is invalid.

## 0.13.0 – 2026-11-02

### New
- **English language:** the whole user interface is available in Norwegian and English. Choose under *Settings → Appearance → Language*;
  the default follows the language of the operating system. The new language is used the next time ShowCue starts.
- English versions of the READMEs and the changelog (`README.md`, `CHANGELOG.md`).
- **Grey theme** (“Grey (soft light)”): light, but less white than Light – easier on the eyes in long sessions.

### Changed
- The program texts have been reviewed in both languages; outdated hints (the ☀ button, menu locations) are fixed.
- Mac: the program icon always shows in the Dock, also when the app is started from Xcode or macOS has an old icon cached.

## 0.12.0 – 2026-11-02

### New
- **Zoom:** make the whole main window larger or smaller (80–200 %) with Ctrl/⌘ and + / −, from the *Setup* menu
  or under *Settings → Appearance*. The choice is remembered.
- **Cue list text:** Normal, Large or Extra large – makes only the cue list larger, in addition to zoom.

### Changed
- **Button panel** (formerly “Cart”): large buttons that start and stop cues directly without moving the GO playhead.
- **Message cue** (formerly “Memo”): shows a message to the operator in the top bar.
- **Equipment** (formerly “Signal out”): the menu for lighting, mixers, media servers and other programs.
- Menu items and buttons no longer end with “…”.

## 0.11.1 – 2026-11-02

### New
- **New logo** (a Selbu rose with a GO symbol): program icon on Windows and Mac, in the installer and under “About ShowCue”.

## 0.11.0 – 2026-11-02

### New
- **Web page cue** (◍): shows a web page or a local .html file full screen on the output, with fade in/out and display time like an image.

### Changed
- **Tidier toolbar:** the buttons are grouped in drop-down menus. All keyboard shortcuts are the same.
