import SwiftUI
import AppKit

/// Skjermbildemodus for manualene: `ShowCue --screenshots <mappe> [--language nb|en]`.
/// Lager et demoshow med egne lyd- og bildefiler, tar bilder av vinduene og avslutter.
/// Kjøres av arbeidsflyten «Skjermbilder» på GitHub.
enum Screenshots {
    static let scale: CGFloat = 2

    private static func argument(_ name: String) -> String? {
        let a = CommandLine.arguments
        guard let i = a.firstIndex(of: name), i + 1 < a.count else { return nil }
        return a[i + 1]
    }

    /// Mappa skjermbildene lagres i (nil = vanlig oppstart).
    static let directory: URL? = argument("--screenshots").map { URL(fileURLWithPath: $0) }
    static var language: String? { directory == nil ? nil : (argument("--language") ?? "nb") }

    private static var log: [String] = []
    private static var windows: [NSWindow] = []

    private static func demo(_ nb: String, _ en: String) -> String { Loc.language == "en" ? en : nb }

    static func run(_ model: AppModel, dir: URL) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let media = URL(fileURLWithPath: "/Users/Shared/Show/Media")
        try? FileManager.default.createDirectory(at: media, withIntermediateDirectories: true)
        var doc = ShowDocument()
        doc.cues = demoCues(media)
        model.setShow(doc, url: media.deletingLastPathComponent().appendingPathComponent(demo("Vårkonsert", "Spring concert") + ".showcue"))
        // Video og PDF i demoen er bare navn (filene lages ikke); de skal ikke vises som manglende.
        model.cues.forEach { $0.error = nil }
        model.statusText = ""

        // Lysbildeserien og innslippsmusikken spiller, så «Aktive cuer» og utgangen viser noe.
        let slides = model.cues.first { $0.type == .slideshow }!
        let audioCues = model.cues.filter { $0.type == .audio }
        model.fire(slides)
        model.fire(audioCues[0])

        let steps: [(Double, () -> Void)] = [
            (3.0, {
                if let e = audioCues[0].error { log.append("lyd: " + e); model.engine.stop(audioCues[0], fade: 0) }
                model.cues.forEach { $0.error = nil }
                model.statusText = ""
                MainWindowCloser.shared.window?.orderOut(nil)
                model.select(audioCues[1])
                // Eget vindu i fast størrelse (et vanlig vindu krympes til skjermen, som er liten på byggemaskinene).
                window(model, title: model.windowTitle, size: NSSize(width: 1440, height: 872)) { MainView() }
            }),
            (2.0, { shoot(windows.first, dir, "hovedvindu") }),
            (0.1, { model.select(slides) }),
            (1.5, { shoot(windows.first, dir, "lysbildeserie") }),
            (0.1, {
                model.settings.theme = .light
                model.select(model.cues.first { $0.type == .video })
            }),
            (1.5, { shoot(windows.first, dir, "hovedvindu-lyst") }),
            (0.1, {
                model.settings.theme = .dark
                model.select(audioCues[1])
                window(model, title: L("Lydpatch"), size: NSSize(width: 1000, height: 620)) { PatchView() }
            }),
            (1.5, { shoot(windows.last, dir, "lydpatch"); windows.last?.close() }),
            (0.1, { window(model, title: L("Innstillinger"), size: NSSize(width: 580, height: 640)) { SettingsView() } }),
            (1.5, { shoot(windows.last, dir, "innstillinger"); windows.last?.close() }),
            (0.2, {
                try? log.joined(separator: "\n").write(to: dir.appendingPathComponent("logg.txt"), atomically: true, encoding: .utf8)
                exit(0)
            }),
        ]
        var at = 0.0
        for (delay, step) in steps {
            at += delay
            DispatchQueue.main.asyncAfter(deadline: .now() + at, execute: step)
        }
    }

    /// Åpner et vindu med visningen (samme miljø som i appen).
    private static func window<V: View>(_ model: AppModel, title: String, size: NSSize, @ViewBuilder _ content: @escaping () -> V) {
        let root = Themed { content() }
            .environmentObject(model)
            .environmentObject(model.audio)
            .environmentObject(model.output)
            .frame(width: size.width, height: size.height)
        let w = FixedSizeWindow(contentRect: NSRect(origin: .zero, size: size), styleMask: [.titled, .closable, .miniaturizable, .resizable],
                                backing: .buffered, defer: false)
        w.title = title
        w.isReleasedWhenClosed = false
        w.contentView = NSHostingView(rootView: root)
        w.setFrameOrigin(NSPoint(x: 0, y: 0))
        w.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        windows.append(w)
    }

    /// Lagrer vinduet (med tittellinje) som PNG i dobbel oppløsning, tegnet av appen selv.
    /// Vindu som ikke krympes til skjermen.
    private final class FixedSizeWindow: NSWindow {
        override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }
    }

    private static func shoot(_ w: NSWindow?, _ dir: URL, _ name: String) {
        guard let w, let content = w.contentView else { log.append(name + ": fant ikke vinduet"); return }
        let view = content.superview ?? content
        let b = view.bounds
        if let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(b.width * scale), pixelsHigh: Int(b.height * scale),
                                      bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                      colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) {
            rep.size = b.size
            view.cacheDisplay(in: b, to: rep)
            if let data = rep.representation(using: .png, properties: [:]) {
                try? data.write(to: dir.appendingPathComponent(name + ".png"))
                log.append("\(name): \(rep.pixelsWide)x\(rep.pixelsHigh)")
            }
        }
    }

    private static func demoCues(_ media: URL) -> [Cue] {
        func wav(_ name: String, _ seconds: Double, _ hz: Double) -> String {
            let url = media.appendingPathComponent(name)
            DemoMedia.writeWav(url, seconds: seconds, baseHz: hz)
            return url.path
        }
        let texts = Loc.language == "en"
            ? ["Welcome", "Tonight's programme", "Interval – 20 min", "Thank you!"]
            : ["Velkommen", "Kveldens program", "Pause – 20 min", "Takk for i kveld!"]
        let slidePaths = texts.enumerated().map { i, t -> String in
            let url = media.appendingPathComponent("lysbilde-\(i + 1).png")
            DemoMedia.writeSlide(url, text: t, index: i)
            return url.path
        }

        var list: [Cue] = []
        func add(_ type: CueType, _ setup: (Cue) -> Void) {
            let c = Cue(type: type)
            c.number = String(list.count + 1)
            setup(c)
            list.append(c)
        }
        add(.audio) { $0.name = demo("Innslipp-musikk", "Walk-in music"); $0.filePath = wav(demo("innslipp.wav", "walk-in.wav"), 185, 220); $0.loop = true; $0.volumeDb = -6; $0.hotkey = "F1"; $0.color = .green }
        add(.fade) { $0.name = demo("Fade ut innslipp", "Fade out walk-in"); $0.target = "1"; $0.fadeTime = 5 }
        add(.memo) { $0.name = demo("Vent på konferansier", "Wait for the host"); $0.text = demo("Vent på at konferansier går av scenen", "Wait for the host to leave the stage") }
        add(.video) { $0.name = demo("Åpningsvideo", "Opening video"); $0.filePath = media.appendingPathComponent(demo("aapning.mp4", "opening.mp4")).path; $0.knownDuration = 92.4; $0.fileChannels = 2; $0.color = .blue }
        add(.osc) { $0.name = demo("Lys: åpning", "Lights: opening"); $0.host = "192.168.1.50"; $0.port = 8000; $0.address = "/lys/cue/1"; $0.preWait = 2; $0.color = .orange }
        add(.slideshow) { $0.name = demo("Program og sponsorer", "Programme and sponsors"); $0.files = slidePaths; $0.slideTime = 30; $0.color = .purple }
        add(.pdf) { $0.name = demo("Sangtekst – vers 1", "Lyrics – verse 1"); $0.filePath = media.appendingPathComponent(demo("sangtekster.pdf", "lyrics.pdf")).path; $0.page = 1 }
        add(.audio) { $0.name = demo("Applaus", "Applause"); $0.filePath = wav(demo("applaus.wav", "applause.wav"), 6.8, 330); $0.hotkey = "F2"; $0.fadeOut = 1; $0.color = .yellow }
        add(.playlist) { $0.name = demo("Pausemusikk", "Interval music"); $0.files = [wav("pause-1.wav", 30, 196), wav("pause-2.wav", 30, 247), wav("pause-3.wav", 30, 262)]; $0.hotkey = "F3"; $0.crossfade = 3 }
        add(.light) { $0.name = demo("Lys: pause", "Lights: interval"); $0.host = "192.168.1.60"; $0.universe = 1; $0.levels = "1-12@60%"; $0.fadeTime = 3 }
        add(.stop) { $0.name = demo("Stopp alt", "Stop all"); $0.fadeTime = 3 }
        return list
    }
}

/// Lager små demofiler (lyd og lysbilder) til skjermbildemodus.
enum DemoMedia {
    /// Rolig akkord med langsom puls (16-bit stereo, 8 kHz) – nok til å gi en bølgeform.
    static func writeWav(_ url: URL, seconds: Double, baseHz: Double) {
        let rate = 8000
        let frames = Int(seconds * Double(rate))
        var d = Data()
        func u32(_ v: Int) { var x = UInt32(v).littleEndian; d.append(Data(bytes: &x, count: 4)) }
        func u16(_ v: Int) { var x = UInt16(v).littleEndian; d.append(Data(bytes: &x, count: 2)) }
        d.append(contentsOf: Array("RIFF".utf8)); u32(36 + frames * 4); d.append(contentsOf: Array("WAVE".utf8))
        d.append(contentsOf: Array("fmt ".utf8)); u32(16); u16(1); u16(2); u32(rate); u32(rate * 4); u16(4); u16(16)
        d.append(contentsOf: Array("data".utf8)); u32(frames * 4)
        d.reserveCapacity(d.count + frames * 4)
        for i in 0..<frames {
            let t = Double(i) / Double(rate)
            let env = 0.55 + 0.35 * sin(2 * .pi * t / 4.3) * sin(2 * .pi * t / 11.0)
            let fade = max(0, min(1, min(t / 0.5, (seconds - t) / 0.8)))
            let v = (sin(2 * .pi * baseHz * t) + 0.5 * sin(2 * .pi * baseHz * 1.5 * t) + 0.3 * sin(2 * .pi * baseHz * 2 * t)) / 1.8
            var s = Int16(v * env * fade * 0.5 * Double(Int16.max)).littleEndian
            let bytes = Data(bytes: &s, count: 2)
            d.append(bytes); d.append(bytes)
        }
        try? d.write(to: url)
    }

    private static let palettes: [(NSColor, NSColor)] = [
        (NSColor(srgbRed: 0x1B / 255, green: 0x2C / 255, blue: 0x44 / 255, alpha: 1), NSColor(srgbRed: 0x0B / 255, green: 0x15 / 255, blue: 0x24 / 255, alpha: 1)),
        (NSColor(srgbRed: 0x24 / 255, green: 0x3B / 255, blue: 0x2F / 255, alpha: 1), NSColor(srgbRed: 0x0E / 255, green: 0x1C / 255, blue: 0x16 / 255, alpha: 1)),
        (NSColor(srgbRed: 0x3A / 255, green: 0x24 / 255, blue: 0x44 / 255, alpha: 1), NSColor(srgbRed: 0x16 / 255, green: 0x0E / 255, blue: 0x1E / 255, alpha: 1)),
        (NSColor(srgbRed: 0x44 / 255, green: 0x2E / 255, blue: 0x1B / 255, alpha: 1), NSColor(srgbRed: 0x1E / 255, green: 0x14 / 255, blue: 0x0B / 255, alpha: 1)),
    ]

    /// Lysbilde i 1920×1080 med mørk fargetone og stor tekst.
    static func writeSlide(_ url: URL, text: String, index: Int) {
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 1920, pixelsHigh: 1080, bitsPerSample: 8, samplesPerPixel: 4,
                                         hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
              let ctx = NSGraphicsContext(bitmapImageRep: rep) else { return }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = ctx
        let p = palettes[index % palettes.count]
        NSGradient(starting: p.0, ending: p.1)?.draw(in: NSRect(x: 0, y: 0, width: 1920, height: 1080), angle: -90)
        let style = NSMutableParagraphStyle()
        style.alignment = .center
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 120, weight: .semibold), .foregroundColor: NSColor.white, .paragraphStyle: style]
        let s = NSAttributedString(string: text, attributes: attrs)
        let h = s.boundingRect(with: NSSize(width: 1700, height: 1000), options: [.usesLineFragmentOrigin]).height
        s.draw(with: NSRect(x: 110, y: 540 - h / 2, width: 1700, height: h), options: [.usesLineFragmentOrigin])
        NSColor(srgbRed: 0x5C / 255, green: 0xD6 / 255, blue: 0x8A / 255, alpha: 1).setFill()
        NSRect(x: 860, y: 540 - h / 2 - 36, width: 200, height: 6).fill()
        NSGraphicsContext.restoreGraphicsState()
        try? rep.representation(using: .png, properties: [:])?.write(to: url)
    }
}
