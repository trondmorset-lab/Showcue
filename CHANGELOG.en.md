# Changelog

*[Norsk](CHANGELOG.md)*

All user-visible changes, newest first. The version number is in the file `VERSION`.
Each version gets its own release on GitHub (`v0.5.0` etc.) with the Windows and Mac programs.
The full history of earlier versions is in the Norwegian changelog.

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
- English versions of the READMEs and the changelog (`README.en.md`, `CHANGELOG.en.md`).
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
