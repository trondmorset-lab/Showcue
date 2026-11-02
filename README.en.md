<img src="assets/showcue-logo.svg" width="128" alt="ShowCue">

# ShowCue

*[Norsk](README.md)*

Show control for Windows and Mac.

A cue-based playback program for concerts, events and theatre.
It comes in two editions that share the show format:

| Folder | Platform | Technology | Building |
|---|---|---|---|
| `Windows/` | Windows 10/11 | C# / .NET 10 / WPF / NAudio | `build.bat`, or Visual Studio |
| `Mac/` | macOS 13+ | Swift / SwiftUI / CoreAudio / AVFoundation | Open `Package.swift` in Xcode, or `./build-app.sh` |

Both have:
- Cues for audio, video, image, PDF, web page, fade, stop, OSC, MIDI, playlist, “go to”, message and network (UDP/TCP/HTTP)
- **Timecode** (MTC/LTC out, MTC triggers in), **lighting over Art-Net** and **live input** through the audio patch
- **Button panel** with large buttons (jingles, effects, touch screens), **loudness normalisation** (LUFS) and **find/collect media files**
- **Pause/resume all**, release loop (devamp), start from the cursor and fade curves
- **Start and end with waveform**, zoom and preview through the audio patch
- Pre-wait, auto-continue and auto-follow
- Hotkeys and MIDI triggers per cue
- **Audio patch** for multichannel audio interfaces and network audio (e.g. Dante), with a routing matrix per cue, test tone and level meters
- **Audio in video** that goes through the audio patch and stays in sync with the picture
- **Remote control** with OSC in, MIDI in and MSC
- **Status out over OSC** (e.g. to Companion): next cue, what is playing and time remaining
- **Light, dark and night mode**, **zoom** and larger text in the cue list, and colour coding of cues
- **Norwegian and English** user interface (chosen under *Settings → Appearance*)

Show files (`.showcue`) can be moved freely between Mac and Windows. Put the media files in the same folder as the show,
or in a subfolder `Media`.

## Download

Ready-built programs are under **Releases** on GitHub:
- **ShowCue x.y.z** – released versions, with the changes in the release notes.
- **Latest build** (“Siste bygg”) – built automatically on every change to main, for testing the newest.

| | Installer | Without installing |
|---|---|---|
| **Windows** | `ShowCue-Setup.exe` – Start menu shortcut, optional desktop icon, `.showcue` files open with ShowCue, uninstall from Settings → Apps | `ShowCue.exe` – a single file that can be copied straight to the show PC |
| **Mac** | `ShowCue-Mac.dmg` – open it and drag ShowCue to *Applications* | `ShowCue-Mac.zip` |

The programs are not code-signed yet:
- **Windows** shows “Windows protected your PC” the first time. Press *More info* → *Run anyway*.
- **Mac** stops the app the first time. Open *System Settings → Privacy & Security*, scroll down and press *Open Anyway*.

## Language

ShowCue follows the language of the operating system: Norwegian on Norwegian systems, English otherwise.
Choose a fixed language under *Settings → Appearance → Language*. The new language is used the next time ShowCue starts.

All texts in the program are written in Norwegian in the source code and translated in `i18n/en.json`
(shared by Windows and Mac). `python3 tools/i18n.py` checks that every text has a translation;
`python3 tools/i18n.py --update` adds new texts and removes unused ones.

## Versions and changelog

The version number is in `VERSION` and is used by both the Windows and the Mac build. Changes are recorded in
`CHANGELOG.md` (Norwegian) and `CHANGELOG.en.md` (English).

How the program is built and released (`.github/workflows/build.yml`):
1. Changes are made on a separate branch and submitted as a *pull request*. GitHub builds and tests both the Windows and the Mac version.
2. When both are green, the change is merged into `main` (which is protected: it only accepts changes that have passed the builds).
3. On `main` everything is built and tested again. Only when both platforms are done are exactly the same files published:
   to “Latest build” every time, and to a new release `v<version>` when `VERSION` has been increased.
4. Every file gets a SHA-256 checksum and a provenance attestation from GitHub. Verify a downloaded file with
   `gh attestation verify ShowCue-Setup.exe -R trondmorset-lab/Showcue-`.

The GitHub Actions steps are pinned to fixed versions (commit SHA), and Dependabot suggests updates every week.

See `Windows/README.en.md` and `Mac/README.en.md` for details.

## Licence

Copyright © 2026 Trond Morset.

ShowCue is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License,
version 3 (GPL-3.0), as published by the Free Software Foundation. See `LICENSE` for the full text.
The program comes with no warranty of any kind.

Components from others and their licences are listed in `THIRD-PARTY-NOTICES.txt`: [NAudio](https://github.com/naudio/NAudio),
the .NET runtime (MIT) and the Microsoft WebView2 SDK (BSD) in the Windows version, and the notice for Art-Net™. The file is
included in the installer, in the Mac app and in every release, and is shown under *About ShowCue* in the program.
Audio and video codecs are provided by the operating system; ShowCue has none of its own.
