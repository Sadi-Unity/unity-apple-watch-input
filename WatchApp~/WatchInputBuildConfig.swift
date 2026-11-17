import Foundation

// Default build configuration for standalone watchOS testing.
// In Unity iOS builds, this file is overwritten by WatchAppPostProcess with settings from WatchInputSettings.
enum WatchInputBuildConfig {
    static let crownHapticsEnabled: Bool = true
    static let ticksPerRange: Double = 24.0
}
