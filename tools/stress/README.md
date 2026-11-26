# Stress test of the program core

Runs the part of the Windows code that does not need WPF (models, audio mixer, playback, OSC/MIDI/MSC,
timecode, DMX, network cues, autosave) over and over with random and corrupted data.
Works on Windows, macOS and Linux. CI runs one round for each pull request.

```
dotnet run -c Release --project tools/stress -- [minutes] [filter]
```

- `minutes` – how long to run (0 = one round, about 45 s). Each round uses new random data (seed = round × 7919).
- `filter` – run only the tests with this in the name, e.g. `Playback`.
- Without NuGet access: `-p:NAudioDll=/path/to/NAudio.dll`.

Failures are written together to `stress-report.txt` next to the program, with one example per kind of failure. If a test
hangs for more than 3 minutes, the run is aborted and the case is written to `hang.txt`. Memory use is printed after each round.

| Test | What it does |
|---|---|
| DmxLevels, Timecode, OSC decode/arguments, MSC parse, Network unescape, Timecode decode | Random and corrupted text/bytes into the parsers – must never throw |
| Remote OSC routing | Random OSC addresses and arguments against the remote control |
| Show roundtrip / mutation / save-load files | Random shows are saved and opened (must be identical), and corrupted show files are opened |
| Playback edge cases | Playback with extreme values (NaN, infinity, empty files, one sample, loop), fades, pause, seeking |
| Mixer concurrency | 8 s of audio thread and control thread at the same time: start/stop, routing, patch, fades, seeking |
| Show log flood | Six threads fill the show log's bounded queue with random text: no errors, the file stays under the limit, old files are deleted, the file can be read back, CSV cells with formula characters get an apostrophe |
| Loudness extremes, Waveform, LTC source, OSC feedback, Dmx fader, Undo history, Safety/Media tools paths | Edge cases |
| Preload planning | Random cue lists (go-to loops, groups, disabled cues) against the preload planner and its accounting: never an exception, never over the limit, no duplicates |
| Network sender | UDP/TCP/HTTP to local receivers and invalid addresses – must never hang or throw |
| Scenes x rehearsal | Random cue lists with scenes, disabled and marked cues: GO through every scene with and without rehearsal mode. The cursor always moves forward, auto-continue never leaves the scene, and preload plans only what GO starts |
| Scene operations | Random add / rename / delete / move / renumber of scenes: the rules hold after every step, and the show file has the same content after open + save |
| Show file new fields | Scenes, routing presets, clock trigger and "Skip in rehearsal": round trip, old files without the fields, and corrupted values |
| Routing presets | Random operations on presets (NaN, infinity, out of range), routes from presets, copy/paste |
| Clock triggers | Parsers with garbage, exactly one firing across midnight, never a firing on clock jumps (daylight saving, setting the clock, sleep) |
| Settings | Settings file with wrong types and extreme numbers: opened or rejected, and the limits hold after `Normalize` |
| ASIO input | Planning with extreme numbers, channel conversion, driver events with NaN/infinity/wrong channel count while listeners are opened and closed |
| Show log dispose | Closing the log while four threads write, and a failing disk (reported once) |
| Web server sockets | The web page against real sockets: malformed and oversized requests, wrong PIN and lock-out, 60 simultaneous and 10 slow connections, a command that throws, stopping with open connections, restart on the same port (takes about 20 s) |
