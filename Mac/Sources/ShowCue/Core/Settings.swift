import Foundation

enum AppTheme: String, Codable, CaseIterable {
    case dark = "Dark", light = "Light", night = "Night"
    var label: String {
        switch self {
        case .dark: return L("Mørk")
        case .light: return L("Lys")
        case .night: return L("Nattmodus (ekstra dempet)")
        }
    }
    var next: AppTheme {
        switch self {
        case .dark: return .light
        case .light: return .night
        case .night: return .dark
        }
    }
}

enum OscFeedbackFormat: String, Codable, CaseIterable {
    case companion = "Companion", generic = "Generic"
    var label: String { self == .companion ? L("Companion (Custom Variables)") : L("Generisk (/showcue/…)") }
}

/// En logisk utgang i lydpatchen.
struct PatchBus: Codable, Hashable, Identifiable {
    var id = UUID()
    var name: String
    var gainDb: Double = 0
    /// Kanaler på lydkortet (fra 1).
    var channels: [Int] = []

    enum CodingKeys: String, CodingKey { case name, gainDb, channels }

    init(name: String, gainDb: Double = 0, channels: [Int] = []) {
        self.name = name; self.gainDb = gainDb; self.channels = channels
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = (try? c.decode(String.self, forKey: .name)) ?? L("Utgang")
        gainDb = (try? c.decode(Double.self, forKey: .gainDb)) ?? 0
        channels = (try? c.decode([Int].self, forKey: .channels)) ?? []
    }

    static func defaultPatch(_ count: Int) -> [PatchBus] {
        (1...count).map { i in
            PatchBus(name: i == 1 ? "Main L" : i == 2 ? "Main R" : L("Utgang {0}", i), channels: [i])
        }
    }
}

/// Maskininnstillinger (lagres i ~/Library/Application Support/ShowCue, ikke i showfila).
struct AppSettings: Codable, Equatable {
    // Lyd
    /// CoreAudio-enhetens UID. Tom = systemets standard utgang.
    var audioDeviceUID = ""
    var maxChannels = 64
    var bufferFrames = 256
    var buses: [PatchBus] = PatchBus.defaultPatch(8)
    /// Egen lydenhet for forhåndslytting i bølgeformen, f.eks. hodetelefonene (CoreAudio-UID). Tom = ingen.
    var previewDeviceUID = ""
    /// Første av to utganger i lydpatchen for forhåndslytting. 0 = cuens egne utganger (salen).
    var previewBus = 0

    // Video
    /// -1 = eget vindu, ellers skjermnummer (0 = hovedskjerm).
    var videoScreen = -1

    // OSC inn
    var oscInEnabled = true
    /// Standard port for OSC inn (8000, vanlig standard for OSC-kontrollere, f.eks. Companion).
    var oscInPort = 8000

    // OSC ut (status)
    var oscFeedbackEnabled = false
    var oscFeedbackHost = "127.0.0.1"
    var oscFeedbackPort = 12321
    var oscFeedbackFormat: OscFeedbackFormat = .companion

    // MIDI inn
    var midiInDevice = ""
    var midiInChannel = 0
    var midiGo = ""
    var midiStopAll = ""
    var midiNext = ""
    var midiPrevious = ""
    var mscInEnabled = true
    var mscInDeviceId = 127
    /// Start cuer med tidskode-trigger fra innkommende MTC (på MIDI-inngangen).
    var mtcInEnabled = false
    /// Lydenhet for live-inngang-cuer (UID, tom = systemets standard inngang).
    var inputDeviceUID = ""

    // Utseende og sikkerhet
    var theme: AppTheme = .dark
    /// Språk i brukerflaten: «nb», «en» eller tom (systemets språk). Tar effekt ved neste oppstart.
    var language = ""
    /// Størrelse på hele hovedvinduet (1 = vanlig), se `UIZoom`.
    var zoom = 1.0
    /// Ekstra tekststørrelse i cuelista, i tillegg til zoom (1 = vanlig).
    var listTextScale = 1.0
    var panicFadeTime = 1.0
    var doubleGoProtectionMs = 300
    /// Mål for «Normaliser lydstyrke» (LUFS). −23 er EBU R128.
    var loudnessTarget = -23.0
    /// Antall kolonner i knappepanelet.
    var buttonPanelColumns = 4

    var lastShowPath = ""

    init() {}

    enum CodingKeys: String, CodingKey {
        case audioDeviceUID, maxChannels, bufferFrames, buses, previewDeviceUID, previewBus, videoScreen, oscInEnabled, oscInPort
        case oscFeedbackEnabled, oscFeedbackHost, oscFeedbackPort, oscFeedbackFormat
        case midiInDevice, midiInChannel, midiGo, midiStopAll, midiNext, midiPrevious, mscInEnabled, mscInDeviceId
        case theme, language, zoom, listTextScale, panicFadeTime, doubleGoProtectionMs, lastShowPath, loudnessTarget, buttonPanelColumns, mtcInEnabled, inputDeviceUID
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = AppSettings()
        func v<T: Decodable>(_ k: CodingKeys, _ def: T) -> T { ((try? c.decodeIfPresent(T.self, forKey: k)) ?? nil) ?? def }
        audioDeviceUID = v(.audioDeviceUID, d.audioDeviceUID)
        maxChannels = v(.maxChannels, d.maxChannels)
        bufferFrames = v(.bufferFrames, d.bufferFrames)
        buses = v(.buses, d.buses)
        if buses.isEmpty { buses = PatchBus.defaultPatch(8) }
        previewDeviceUID = v(.previewDeviceUID, d.previewDeviceUID)
        previewBus = v(.previewBus, d.previewBus)
        videoScreen = v(.videoScreen, d.videoScreen)
        oscInEnabled = v(.oscInEnabled, d.oscInEnabled)
        oscInPort = v(.oscInPort, d.oscInPort)
        oscFeedbackEnabled = v(.oscFeedbackEnabled, d.oscFeedbackEnabled)
        oscFeedbackHost = v(.oscFeedbackHost, d.oscFeedbackHost)
        oscFeedbackPort = v(.oscFeedbackPort, d.oscFeedbackPort)
        oscFeedbackFormat = v(.oscFeedbackFormat, d.oscFeedbackFormat)
        midiInDevice = v(.midiInDevice, d.midiInDevice)
        midiInChannel = v(.midiInChannel, d.midiInChannel)
        midiGo = v(.midiGo, d.midiGo)
        midiStopAll = v(.midiStopAll, d.midiStopAll)
        midiNext = v(.midiNext, d.midiNext)
        midiPrevious = v(.midiPrevious, d.midiPrevious)
        mscInEnabled = v(.mscInEnabled, d.mscInEnabled)
        mscInDeviceId = v(.mscInDeviceId, d.mscInDeviceId)
        theme = v(.theme, d.theme)
        language = v(.language, d.language)
        zoom = UIZoom.clamp(v(.zoom, d.zoom))
        listTextScale = UIZoom.clampList(v(.listTextScale, d.listTextScale))
        panicFadeTime = v(.panicFadeTime, d.panicFadeTime)
        doubleGoProtectionMs = v(.doubleGoProtectionMs, d.doubleGoProtectionMs)
        lastShowPath = v(.lastShowPath, d.lastShowPath)
        loudnessTarget = v(.loudnessTarget, d.loudnessTarget)
        buttonPanelColumns = v(.buttonPanelColumns, d.buttonPanelColumns)
        mtcInEnabled = v(.mtcInEnabled, d.mtcInEnabled)
        inputDeviceUID = v(.inputDeviceUID, d.inputDeviceUID)
    }

    /// Nøkkel for lydoppsettet: bare endringer her krever omstart av lyden.
    var audioKey: String { "\(audioDeviceUID)|\(maxChannels)|\(bufferFrames)" }

    static var fileURL: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory())
        return base.appendingPathComponent("ShowCue").appendingPathComponent("settings.json")
    }

    static func load() -> AppSettings {
        guard let data = try? Data(contentsOf: fileURL),
              let s = try? JSONDecoder().decode(AppSettings.self, from: data) else { return AppSettings() }
        return s
    }

    func save() {
        do {
            try FileManager.default.createDirectory(at: AppSettings.fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            let e = JSONEncoder()
            e.outputFormatting = [.prettyPrinted]
            try e.encode(self).write(to: AppSettings.fileURL, options: .atomic)
        } catch {
            NSLog(L("ShowCue: kunne ikke lagre innstillinger: {0}", error))
        }
    }
}

/// Trinn for zoom av hovedvinduet og tekststørrelse i cuelista.
enum UIZoom {
    static let steps: [Double] = [0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0]
    static let listSizes: [(label: String, scale: Double)] = [(L("Vanlig"), 1.0), (L("Stor"), 1.25), (L("Ekstra stor"), 1.5)]

    static func clamp(_ z: Double) -> Double { z.isFinite ? min(steps.last!, max(steps.first!, z)) : 1 }
    static func clampList(_ s: Double) -> Double { s.isFinite ? min(2, max(1, s)) : 1 }

    /// Neste trinn opp (`direction` > 0) eller ned fra `z`.
    static func step(_ z: Double, _ direction: Int) -> Double {
        if direction > 0 { return steps.first { $0 > z + 0.001 } ?? steps.last! }
        return steps.last { $0 < z - 0.001 } ?? steps.first!
    }

    static func percent(_ z: Double) -> String { "\(Int((z * 100).rounded())) %" }
}
