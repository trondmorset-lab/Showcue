import Foundation
import AppKit

/// Forhåndssjekkens oppslag mot den faktiske maskinen: filsystem, lydenheter og skjermer.
struct SystemPreflightEnvironment: PreflightEnvironment {
    func fileStatus(_ path: String) -> PreflightFileStatus {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory), !isDirectory.boolValue else { return .missing }
        return FileManager.default.isReadableFile(atPath: path) ? .ok : .unreadable
    }

    func audioOutputExists(uid: String) -> Bool { CoreAudioDevices.outputDevices().contains { $0.uid == uid } }

    func inputDeviceExists(uid: String) -> Bool { CoreAudioDevices.inputDevices().contains { $0.uid == uid } }

    func screenExists(_ index: Int) -> Bool { index >= 0 && index < NSScreen.screens.count }
}
