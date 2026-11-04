# Changelog

*[Norsk](CHANGELOG.md)*

All user-visible changes, newest first. The version number is in the file `VERSION`.
Each version gets its own release on GitHub (`v0.5.0` etc.) with the Windows and Mac programs.
The full history of earlier versions is in the Norwegian changelog.

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
