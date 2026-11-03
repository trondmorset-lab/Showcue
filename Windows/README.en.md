# ShowCue

*[Norsk](README.md)*

A cue-based playback program for Windows. Made for concerts and events:
a GO button with a cue list, hotkeys for jingles and walk-in, and output to a projector.

## Building the program

1. Install the **.NET 10 SDK** (not just the runtime): https://dotnet.microsoft.com/download/dotnet/10.0
2. Double-click `build.bat`.
3. Finished program: `publish\ShowCue.exe` (a single file, no installation needed – can be copied to the show PC).

Alternatively: open `ShowCue.sln` in Visual Studio 2022 (17.14 or newer) or 2026 and press F5.
The tests are in `ShowCue.Tests` and run with `dotnet test ShowCue.Tests` (or Test Explorer in Visual Studio).
GitHub Actions builds both `ShowCue.exe` and the installer `ShowCue-Setup.exe` (Inno Setup, the script is in
`installer/ShowCue.iss`) and puts them under *Releases*. The installer creates a Start menu shortcut,
an optional desktop icon and associates `.showcue` files with ShowCue, and can install for one user without administrator rights.

Requires Windows 10 (1809) or newer, 64-bit.

## Cue types

| Type | What it does |
|---|---|
| ♪ **Audio** | WAV, MP3, AIFF, FLAC, M4A/AAC, WMA. Level (dB), start/end point, loop, fade in/out. |
| ▶ **Video** | MP4, MOV, WMV, AVI, MKV. The audio is extracted and goes through the audio patch (see below). Start/end point, loop, fade, crossfade. |
| ▣ **Image** | PNG, JPG, BMP, GIF, TIFF. Optional display time, fade, crossfade. |
| ▤ **PDF** | Shows one page. “+ Cue for next page” quickly creates one cue per page (lyrics, presentations). |
| ◢ **Fade** | Fades a running cue to a new audio level and/or opacity, optionally stopping it afterwards. |
| ■ **Stop** | Stops one cue (or everything, if the target is empty), with an optional fade. |
| ⇢ **OSC** | Sends OSC over UDP to a mixer, lighting desk, Companion etc. Supports int, float, text, true/false. |
| ♫ **MIDI** | Note on/off, program change, CC and MSC GO (MIDI Show Control). |

All cues have **pre-wait** (delay before start), **auto-continue** (start the next at the same time),
**auto-follow** (start the next when this one finishes), **hotkey**, **MIDI trigger** and **notes**.

### Start and end (waveform)

Audio and video cues have a waveform in the inspector where the start and end points are edited visually:

- Drag the **green** marker (start) and the **red** one (end). The area outside is darkened, and fade in/out are shown as orange lines.
- **Click** moves the blue cursor, **double-click** listens from there. The **mouse wheel** zooms around the pointer, **Shift + wheel** scrolls.
- **▶ From start**, **▶ From cursor** and **▶ The end** (the last 5 seconds before the end point) listen through the audio patch without affecting the show.
- **Start = cursor** / **End = cursor** set the point where the cursor is – or where playback is right now, so you can press while it plays.
- **⤢ Large window** opens a large editing view. An end at the very end of the file is stored as “0 = end of file”.
- For video, picture and audio start at the start point, and with loop the video jumps back to the start point.

## Audio patch and multichannel audio interfaces

The audio goes through three stages:

```
file channels ──(routing per cue)──▶ outputs ──(audio patch)──▶ channels on the audio interface
```

- **Outputs** (buses) are logical, e.g. “Main L”, “Main R”, “Monitor”, “Recording”. The show file only refers
  to the outputs, so the show can be moved to another computer with a different audio interface – only the patch needs changing.
- **Audio patch** (*Setup → Audio patch*): one row per output and one column per channel on the audio interface. Tick
  where each output goes. One output can go to several channels. Level per output in dB.
  **Test** plays pink noise (−18 dBFS) on the output for a line check, and level meters are shown per output and per channel.
  With ASIO, the channel names from the driver (e.g. the Dante channels) are shown when you hover over the column header.
- **Routing per audio cue**: the matrix in the panel on the right, with file channels as rows and outputs as columns. Enter the level in dB,
  or double-click a cell to turn it on/off. “Default” means mono → 1+2, stereo → 1/2, and multichannel files channel by channel.
  Changes to routing and level are heard immediately while the cue is playing.

**Audio in video (“de-embedding”):** the audio track in video files is decoded by Windows (Media Foundation: AAC, PCM, WMA …)
and played by ShowCue's own audio engine. It goes through the audio patch just like an audio cue, with
routing matrix, level in dB, fader and fade cues. The audio drives the timing: if the picture drifts more than 0.12 s
(e.g. because the audio interface clock and the PC clock run slightly differently), the picture is adjusted. The button **“Extract audio to its own audio cue”**
saves the audio track as WAV next to the video and creates an audio cue that starts at the same time. The audio can then be edited
on its own (start/end, loop) or replaced with a separate mix.

**Example – Dante Virtual Soundcard:**
1. Start DVS in **ASIO mode** and choose the number of channels (e.g. 64×64) and the sample rate in Dante Controller.
2. ShowCue → *Settings → Audio*: choose **ASIO** and the driver “Dante Virtual Soundcard (x64)”.
3. *Audio patch*: tick which Dante channels each output goes to. Route the channels onwards in Dante Controller.

DVS in WDM mode (and other multichannel interfaces without ASIO) also works via **WASAPI**. The device's channels
and sample rate are then used directly. ASIO is recommended for the lowest latency.

## Remote control: OSC and MIDI in

**OSC** (UDP, port 8000 by default – the same as the Companion module “Generic OSC” sends to):

| Address | Function |
|---|---|
| `/go` | GO |
| `/stop` | Stop all (with panic fade) |
| `/panic` | Like Esc (twice within 1 s = immediately) |
| `/hardstop` | Stop all immediately |
| `/next`, `/previous` | Move the GO playhead |
| `/playhead/{no}` or `/select/{no}` | Set the playhead to a cue |
| `/cue/{no}/start` | Start a cue (the playhead does not move) |
| `/cue/{no}/stop` | Stop a cue (with the cue's fade out) |
| `/cue/{no}/go` or `/go/{no}` | Set the playhead to a cue and run GO |
| `/hotkey/{key}` | Like pressing the hotkey, e.g. `/hotkey/F1` |
| `/pause`, `/resume`, `/pause/toggle` | Pause / resume all |
| `/cue/{no}/devamp` | Release the loop in a cue (finishes and continues to the end of the file) |

- The cue number can also be sent as an argument: `/cue/start "12"`.
- Messages with argument 0 or false are ignored, so buttons that send “released” do not trigger twice.
- The prefix `/showcue/...` is also allowed. OSC bundles are supported.
- The status bar at the bottom shows the port, the computer's IP addresses and the last message received.
- The Windows firewall asks the first time: allow ShowCue on private networks.

**Bitfocus Companion / Stream Deck:** use the module “Generic OSC”, set the target IP to the show PC (port 8000 is already the module's default),
and send e.g. `/go` or `/cue/5/start`.

### Status out over OSC

*Settings → OSC → Status out*: enter the IP address of the receiver and the port. For Companion it is port **12321**
(Companion's OSC port; turn on OSC under *Settings → Protocols* in Companion).

**Format “Companion”** sets custom variables. Create these under *Variables → Custom Variables* in Companion
(you only need the ones you want to use):

| Variable | Content | Example |
|---|---|---|
| `sc_next_number`, `sc_next_name`, `sc_next_notes` | The cue at the GO playhead | `3`, `Intro video` |
| `sc_playing`, `sc_playing_number` | Most recently started cue that is playing | `Applause jingle` |
| `sc_playing_all` | Everything that is playing | `Walk-in · Applause jingle` |
| `sc_playing_count` | Number playing | `2` |
| `sc_remaining`, `sc_remaining_name` | Time remaining on the cue that ends first | `-0:05` |
| `sc_elapsed` | Time played on the most recently started cue | `1:23` |
| `sc_running` | Numbers of everything playing | `1,7` |
| `sc_playhead` | Position of the playhead | `3/11` |
| `sc_state` | `klar` (ready), `venter` (pre-wait) or `spiller` (playing) | |
| `sc_show` | Name of the show | |

Use them in button text, e.g. `$(internal:custom_sc_next_name)`, and in feedback (e.g. a red button when
`sc_state` = `spiller`). Only changed values are sent (about 10 times a second), plus everything every 5 seconds.
Companion can request everything again with `/refresh`.

**Format “Generic”** sends `/showcue/next/name`, `/showcue/remaining`, `/showcue/playing/count` (integer) etc.,
plus `/showcue/cue/{no}/running 1|0` when a cue starts and stops. Suits other programs or modules that listen for OSC.

**MIDI in** (*Settings → MIDI in*):
- Choose the input and channel (or all).
- **Fixed functions:** GO, Stop all, Next and Previous can be assigned to a note, CC or program change with **Learn**.
- **Per cue:** “MIDI trigger” in the panel on the right. Press **Learn** and then a key/button on the controller.
  The cue is started, or stopped if it is already playing, just like a hotkey.
- CC triggers when the value goes from below 64 to 64 or more, so a fader does not trigger all the time.
- **MIDI Show Control:** GO and STOP with a cue number from lighting desks etc., with an optional device ID.

OSC and MIDI **out** are done with OSC and MIDI cues in the cue list.

## Operation

| Key | Function |
|---|---|
| **Space** | GO – run the cue at the playhead and move to the next |
| **Esc** | Stop all with a short fade. Press again within 1 s to stop immediately |
| **↑ / ↓** | Move the GO playhead |
| **F1–F12, A–Z, 0–9** | Hotkeys (start/stop the cue without moving the playhead) |
| **Ctrl+1 … Ctrl+8** | Add audio, video, image, PDF, fade, stop, OSC, MIDI |
| **Ctrl+9 / Ctrl+0** | Add playlist / go to cue |
| **Ctrl+P** | Pause / resume all |
| **Ctrl+Shift+M / Ctrl+Shift+N** | Add message / network cue |
| **Ctrl+K** | Button panel |
| **Ctrl + / Ctrl −** | Zoom in / out (normal size in the *Setup* menu) |
| **Ctrl+Shift+T / L / I / W** | Add timecode, light, input, web page cue |
| **Ctrl+D** | Duplicate |
| **Ctrl+↑ / Ctrl+↓** | Move cue up/down |
| **Delete** | Delete selected cues |
| **Ctrl+S / Ctrl+O / Ctrl+N** | Save / open / new show |
| **Ctrl+Z / Ctrl+Y** | Undo / redo (also Ctrl+Shift+Z) |
| **Ctrl+L** | Show mode on/off |
| **Enter** (in a field) | Confirm and go back to the list |

- **Drag files** from Explorer straight into the cue list. Drop one file on a media cue without a file, and the cue gets the file.
- **＋ Media** creates an empty cue (audio, video, image, PDF, playlist); choose the file later with *Browse* in the panel on the right.
- **Double-click** a cue to test it without moving the playhead.
- Hotkeys on audio/image/video work as on/off: press once to start, once more to stop (with the cue's fade out).
- The F keys also work while you are typing in a field; letters and digits do not.
- GO has double-GO protection (300 ms) so key bounce does not skip a cue.

## Pause, playlists and loop

- **Pause** (the button next to STOP ALL or Ctrl+P): freezes all audio, video, fades and pre-waits. Press again, or GO, to resume.
- **Playlist:** a cue with several audio files played one after another with crossfades. Choose random order and
  whether the list should start over. Fade out is used when it is stopped. Good for walk-in and intervals.
- **Go to:** moves the GO playhead to another cue (e.g. back to the start of a sequence), and can run it straight away.
- **Release loop (devamp):** tick “Release loop” in a stop cue. The target cue (audio, video or playlist) finishes
  the current pass and continues to the end of the file instead of stopping. The ⤼ button in the panel does the same for the selected cue.
- **Scrubbing while playing:** audio and video cues under *Active cues* have a scrub bar. Drag it to scrub in
  real time, or jump 5 seconds with ⏪ / ⏩. Picture and sound in a video follow each other. Also works while paused.
- **Run from here:** the button below the waveform starts the cue from the blue cursor, on its normal outputs – useful in rehearsals.
- **Fade curves:** fade cues can use *smooth* (linear in dB, default), *S-curve* (soft start and end) or
  *linear amplitude* (like a physical fader).

## The toolbar

| Menu | Content |
|---|---|
| **File** | New, open, save, collect show, find missing media files |
| **＋ Media** | Audio, video, image, PDF, web page, playlist, live input |
| **Control** | Fade, stop, go to, message |
| **Equipment** | Control other equipment: OSC, MIDI, network, timecode, lighting (Art-Net) |
| **Edit** | Duplicate, delete, move, renumber, normalise loudness |
| **Setup** | Audio patch, output window, settings, theme, zoom, about ShowCue |

Undo/redo, **▦ Button panel** and **🔒 Show mode** are separate buttons. The hotkeys are shown in the menus.

**Web page cue** (◍): shows a web page (e.g. `https://time.is`) or a local .html file full screen on the output,
with fade and display time like an image. Windows uses Microsoft Edge WebView2, which is built into Windows 10 and 11.
Audio from the page goes to the system audio output, not through the audio patch.

## Button panel, network, message and loudness

- **Button panel** (▦ Button panel or Ctrl+K): a separate window with large buttons that start and stop cues directly,
  without moving the GO playhead – useful for jingles, sound effects and applause stings, and on touch screens. Shows cues with
  a hotkey and cues marked “Show in button panel”. Press to start, press again to stop (with the cue's fade out).
  The number of columns can be adjusted.
- **Network cue** (⇄ Network): sends text over **UDP** or **TCP**, or an **HTTP** request. `\r`, `\n`, `\t`
  and `\xHH` in the message give line break, tab and single bytes. Example PJLink projector off: TCP, port 4352, `%1POWR 0\r`.
  Errors (e.g. no connection) are shown on the cue; GO never waits for the network.
- **Message cue** (✎ Message): a message to the operator (“Wait for applause”) shown large at the top until it is closed.
- **Normalise loudness:** *Normalise* in the panel for an audio cue, or *Edit → Normalise loudness*, measures
  the loudness (integrated LUFS according to EBU R128) between start and end and sets the level so the cue hits the target
  (default −23 LUFS, changed under *Settings → Safety*).

## Timecode, lighting and live input

- **Timecode cue** (⏱ TC): sends MTC to a MIDI output and/or LTC as audio to a separate output in the audio patch,
  from the start time (e.g. 01:00:00:00). Length 0 = until the cue is stopped. Pause stops the timecode.
- **Timecode trigger:** enter a time (hh:mm:ss:ff) in the panel, and turn on *MTC in* under *Settings → MIDI*.
  The cue starts when incoming MIDI Time Code passes the time (not when skipping past it).
- **Light cue** (✺ Light): sends DMX over Art-Net (UDP 6454) to a node or as a broadcast. Levels are written
  `channel@value`, e.g. `1@255 2-4@50% 10@0`, and fade over the chosen time. Channels not mentioned keep their level.
- **Live input** (◉ In): the audio from the input chosen under *Settings → Audio* (WASAPI: microphone, line in,
  network audio interface in WDM mode) goes through the routing matrix and the audio patch with level and fades. Latency about 20–60 ms.
  ASIO inputs are not supported yet – use the network audio interface in WDM mode.

## Media files: find and collect

- If files are missing when a show is opened, ShowCue offers to search a folder you choose (with subfolders), by file name.
  The same is available under *File → Find missing media files*.
- *File → Collect the show and media files in one folder* copies the show and all the files it uses to a folder, with the files in
  `Media/`. Copy the folder to a USB stick or another computer (also a Mac), and the show finds the files by itself.

## Safe during the show

- **Show mode** (the 🔒 button or Ctrl+L): locks all editing. GO, stop, Esc, hotkeys, remote control and
  quick buttons work as usual, but nothing can be moved, deleted or changed by accident. The title bar shows SHOW MODE.
- **Undo/redo** (Ctrl+Z / Ctrl+Y, ↶ ↷ in the toolbar): undoes changes to the show – new, deleted and moved cues
  and all fields. In a text field, Ctrl+Z undoes the typing in the field.
- **Autosave:** unsaved changes are written every 30 seconds to `%AppData%\ShowCue\Autolagring`. If the program crashes
  or the power goes, you are offered to recover the changes the next time the show is opened.
- **Backups:** every time you save, the previous version is copied to `%AppData%\ShowCue\Sikkerhetskopier`
  (the last 20 per show are kept).
- **Preview to headphones:** under *Settings → Audio* you choose a separate audio device (e.g. the PC's
  headphone output) or an output in the audio patch for listening in the waveform. Then the preview is not heard in the auditorium.
  Without this, the preview goes to the cue's normal outputs, and the waveform shows a warning.

## Appearance

Four colour themes, switched immediately via *Setup → Switch theme* or *Settings → Appearance*:
**Dark** (graphite), **Grey** (soft and less white than Light), **Light** (daylight and rehearsals) and **Night mode** (extra dimmed for dark scenes).
The Windows title bar follows the theme.

**Zoom and text size:** *Setup → Zoom in / Zoom out* (Ctrl + and Ctrl −) makes the whole main window 80–200 % in size.
*Setup → Cue list text* (Normal, Large, Extra large) makes only the cue list larger, in addition.
Both are also under *Settings → Appearance* and are remembered until next time.

**Language:** Norwegian or English under *Settings → Appearance → Language*. The default follows the Windows language
(Norwegian on Norwegian computers, English otherwise). The new language is used the next time ShowCue starts.

**Cue colours:** give cues a colour (red, orange, yellow, green, blue, purple, grey) in the panel on the right. The row is tinted,
and the quick button gets a colour stripe. Select several cues to colour them all at once.
The colours are adapted to each theme (muted in dark and night, pastel in grey and light).

## Settings

- **Audio:** WASAPI (standard audio interfaces, USB and network audio interfaces in WDM mode) or **ASIO** (pro audio interfaces and network audio interfaces).
  Falls back automatically to the Windows default device if the chosen interface is missing. See *Audio patch* above.
- **OSC in / MIDI in:** see *Remote control* above.
- **Video/image/PDF output:** choose the screen/projector for full screen, or “Window” for testing on one screen.
  Each visual cue can also have its own screen (*Screen* in the panel on the right); *Default* uses the screen from here.
  The output is also shown as a preview at the bottom right.
- Settings and the audio patch are stored per computer in `%AppData%\ShowCue\settings.json`.

## Show files

Shows are saved as `.showcue` (readable JSON). If you move the show to another computer, put the media files
in the same folder as the show file or in a subfolder `Media` – then ShowCue finds them automatically.
Missing files are marked with a red **!** in the list.

## Mac version

There is a separate Mac version in the folder `Mac/`. The show files can be shared: shows saved on a Mac open in Windows version 0.4+,
and shows from Windows open on a Mac. ShowCue finds the media files again if they are next to the show file or in a subfolder `Media`.

## Known limitations

- Video is played with Windows' built-in media engine. MP4/H.264 with AAC audio is safest; some MKV/HEVC files
  may need codecs from the Microsoft Store. Videos with several audio tracks: only the first one is used.
- Picture and audio start together once the video has been opened. Large 4K files may take a moment to open – consider adding
  a small pre-wait on the cue before, rather than pressing GO at the last second.
- No group cues yet.

## Folder structure

```
ShowCue/
  Models/      Cue types, show file, settings and language (Loc)
  Engine/      Audio engine: PatchMixer (buses → channels), CuePlayback (routing, fades),
               AudioEngine (WASAPI/ASIO), VideoAudio, OSC in/out, OscFeedback, MIDI in/out,
               RemoteControl, cue engine
  Views/       Output window, settings, audio patch, routing matrix, theme switching
  Themes/      Colors.Dark/Light/Night.xaml (colours) and Controls.xaml (styles)
  MainWindow   Main window: cue list, GO, inspector, active cues, hotkeys
```
