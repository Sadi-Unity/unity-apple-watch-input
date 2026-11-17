#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WatchInput.Editor
{
    /// <summary>
    /// Editor-only configuration storage for the watchOS companion target.
    /// Persisted at ProjectSettings/WatchInputSettings.asset and tracked in version control.
    /// </summary>
    [FilePath("ProjectSettings/WatchInputSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class WatchInputSettings : ScriptableSingleton<WatchInputSettings>
    {
        public const string WatchTargetName = "WatchInput Watch App";
        public const string SubfolderName = "WatchInputWatchApp";

        [SerializeField]
        [Tooltip("Display name shown on the Apple Watch home screen. Defaults to PlayerSettings.productName if empty.")]
        private string m_WatchAppDisplayName = "";

        [SerializeField]
        [Tooltip("Appended to the host iOS application bundle identifier (e.g. watchkitapp).")]
        private string m_BundleIdSuffix = "watchkitapp";

        [SerializeField]
        [Tooltip("Square 1024x1024 PNG icon without alpha channel. Required for App Store submission.")]
        private Texture2D m_AppIcon;

        [SerializeField]
        [Tooltip("Minimum watchOS deployment version. Single-target SwiftUI companion apps require watchOS 9.0 or higher.")]
        private string m_MinimumWatchOS = "9.0";

        [SerializeField]
        [Tooltip("Enables or disables haptic detent clicks during Digital Crown rotation.")]
        private bool m_CrownHapticsEnabled = true;

        [SerializeField]
        [Tooltip("Crown haptic detents per crown range. Higher = finer steps.")]
        private int m_CrownTickResolution = 24;

        [SerializeField]
        [Tooltip("Provisioning profile specifier or UUID for watch app. Used only when manual signing is selected in Player Settings.")]
        private string m_WatchProvisioningProfileId = "";

        public string WatchAppDisplayName
        {
            get => m_WatchAppDisplayName;
            set => m_WatchAppDisplayName = value;
        }

        public string BundleIdSuffix
        {
            get => string.IsNullOrEmpty(m_BundleIdSuffix) ? "watchkitapp" : m_BundleIdSuffix;
            set => m_BundleIdSuffix = value;
        }

        public Texture2D AppIcon
        {
            get => m_AppIcon;
            set => m_AppIcon = value;
        }

        public string MinimumWatchOS
        {
            get => string.IsNullOrEmpty(m_MinimumWatchOS) ? "9.0" : m_MinimumWatchOS;
            set => m_MinimumWatchOS = value;
        }

        public bool CrownHapticsEnabled
        {
            get => m_CrownHapticsEnabled;
            set => m_CrownHapticsEnabled = value;
        }

        public int CrownTickResolution
        {
            get => Mathf.Clamp(m_CrownTickResolution, 4, 60);
            set => m_CrownTickResolution = Mathf.Clamp(value, 4, 60);
        }

        public string WatchProvisioningProfileId
        {
            get => m_WatchProvisioningProfileId;
            set => m_WatchProvisioningProfileId = value;
        }

        public void Save()
        {
            Save(true);
        }
    }
}
#endif
