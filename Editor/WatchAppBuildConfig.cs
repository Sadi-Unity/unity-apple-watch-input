#if UNITY_IOS
namespace WatchInput.Editor
{
    /// <summary>
    /// Central configuration constants for the watchOS companion target.
    /// Provides initial build-time defaults prior to introducing user-facing settings assets.
    /// </summary>
    public static class WatchAppBuildConfig
    {
        public const string WatchTargetName = "WatchInput Watch App";
        public const string DisplayName = "Watch Input";
        public const string WatchOSDeploymentTarget = "9.0";
        public const string SubfolderName = "WatchInputWatchApp";
    }
}
#endif
