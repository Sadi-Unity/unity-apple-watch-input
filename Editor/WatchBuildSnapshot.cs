#if UNITY_EDITOR
using System.IO;
using UnityEditor;

namespace WatchInput.Editor
{
    /// <summary>
    /// Immutable snapshot of host player settings and watch input configurations for target generation.
    /// Decouples Xcode post-processing logic from static PlayerSettings and AssetDatabase lookups.
    /// </summary>
    internal struct WatchBuildSnapshot
    {
        // Host PlayerSettings values
        public string ApplicationIdentifier;
        public string ProductName;
        public string BundleVersion;
        public string BuildNumber;
        public string AppleDeveloperTeamId;
        public bool AppleEnableAutomaticSigning;

        // WatchInputSettings values
        public string WatchAppDisplayName;
        public string BundleIdSuffix;
        public string MinimumWatchOS;
        public bool CrownHapticsEnabled;
        public int CrownTickResolution;
        public string WatchProvisioningProfileId;

        // Resolved file and directory paths
        public string AppIconSourcePath;
        public string WatchSourcesDirectory;

        public static WatchBuildSnapshot Create(WatchInputSettings settings, string packageRoot = null)
        {
            string iconPath = null;
            if (settings.AppIcon != null)
            {
                iconPath = AssetDatabase.GetAssetPath(settings.AppIcon);
            }

            string sourcesDir = null;
            if (!string.IsNullOrEmpty(packageRoot))
            {
                sourcesDir = Path.Combine(packageRoot, "WatchApp~");
            }

            return new WatchBuildSnapshot
            {
                ApplicationIdentifier = PlayerSettings.applicationIdentifier,
                ProductName = PlayerSettings.productName,
                BundleVersion = PlayerSettings.bundleVersion,
                BuildNumber = PlayerSettings.iOS.buildNumber,
                AppleDeveloperTeamId = PlayerSettings.iOS.appleDeveloperTeamID,
                AppleEnableAutomaticSigning = PlayerSettings.iOS.appleEnableAutomaticSigning,

                WatchAppDisplayName = settings.WatchAppDisplayName,
                BundleIdSuffix = settings.BundleIdSuffix,
                MinimumWatchOS = settings.MinimumWatchOS,
                CrownHapticsEnabled = settings.CrownHapticsEnabled,
                CrownTickResolution = settings.CrownTickResolution,
                WatchProvisioningProfileId = settings.WatchProvisioningProfileId,
                AppIconSourcePath = iconPath,
                WatchSourcesDirectory = sourcesDir
            };
        }
    }
}
#endif
