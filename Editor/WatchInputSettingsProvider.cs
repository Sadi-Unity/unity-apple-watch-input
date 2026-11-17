#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WatchInput.Editor
{
    /// <summary>
    /// Implements the "Project/Watch Input" panel inside the Unity Project Settings window.
    /// Provides inspector UI, live validation feedback, and signing inheritance summaries.
    /// </summary>
    public static class WatchInputSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider("Project/Watch Input", SettingsScope.Project)
            {
                label = "Watch Input",
                guiHandler = (searchContext) =>
                {
                    var settings = WatchInputSettings.instance;
                    var serializedObject = new SerializedObject(settings);
                    serializedObject.Update();

                    EditorGUI.BeginChangeCheck();

                    DrawHeader();
                    DrawIdentitySection(serializedObject, settings);
                    EditorGUILayout.Space(10);

                    DrawIconSection(serializedObject, settings);
                    EditorGUILayout.Space(10);

                    DrawCrownSection(serializedObject, settings);
                    EditorGUILayout.Space(10);

                    DrawSigningSection(serializedObject, settings);

                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedObject.ApplyModifiedProperties();
                        settings.Save();
                    }
                },
                keywords = new HashSet<string>(new[] { "Watch", "Apple", "WatchOS", "Input", "Crown", "Haptics", "Signing" })
            };

            return provider;
        }

        private static void DrawHeader()
        {
            EditorGUILayout.LabelField("Watch Input Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Configure watchOS companion app properties, signing inheritance, and Digital Crown settings.", MessageType.None);
            EditorGUILayout.Space(5);
        }

        private static void DrawIdentitySection(SerializedObject serializedObject, WatchInputSettings settings)
        {
            EditorGUILayout.LabelField("Application Identity", EditorStyles.boldLabel);

            var nameProp = serializedObject.FindProperty("m_WatchAppDisplayName");
            EditorGUILayout.PropertyField(nameProp, new GUIContent("Display Name", "Display name shown on the Apple Watch home screen."));
            if (string.IsNullOrWhiteSpace(nameProp.stringValue))
            {
                EditorGUILayout.HelpBox($"Empty: Defaulting to PlayerSettings.productName ('{PlayerSettings.productName}').", MessageType.Info);
            }

            var suffixProp = serializedObject.FindProperty("m_BundleIdSuffix");
            EditorGUILayout.PropertyField(suffixProp, new GUIContent("Bundle ID Suffix", "Appended to host bundle identifier (e.g. .watchkitapp)."));

            var bundleIssues = new List<ValidationIssue>();
            WatchInputValidator.ValidateBundleSuffix(settings, bundleIssues);
            DrawIssues(bundleIssues);

            string fullBundleId = $"{PlayerSettings.applicationIdentifier}.{settings.BundleIdSuffix}";
            EditorGUILayout.LabelField("Resolved Watch Bundle ID", fullBundleId, EditorStyles.miniLabel);

            var minOSProp = serializedObject.FindProperty("m_MinimumWatchOS");
            EditorGUILayout.PropertyField(minOSProp, new GUIContent("Minimum watchOS", "Minimum deployment target version."));

            var osIssues = new List<ValidationIssue>();
            WatchInputValidator.ValidateMinimumWatchOS(settings, osIssues);
            DrawIssues(osIssues);
        }

        private static void DrawIconSection(SerializedObject serializedObject, WatchInputSettings settings)
        {
            EditorGUILayout.LabelField("Watch App Icon", EditorStyles.boldLabel);

            var iconProp = serializedObject.FindProperty("m_AppIcon");
            EditorGUILayout.PropertyField(iconProp, new GUIContent("App Icon (1024x1024)", "Square 1024x1024 PNG icon with no alpha channel."));

            var iconIssues = new List<ValidationIssue>();
            WatchInputValidator.ValidateAppIcon(settings, iconIssues);
            DrawIssues(iconIssues);
        }

        private static void DrawCrownSection(SerializedObject serializedObject, WatchInputSettings settings)
        {
            EditorGUILayout.LabelField("Digital Crown", EditorStyles.boldLabel);

            var hapticsProp = serializedObject.FindProperty("m_CrownHapticsEnabled");
            EditorGUILayout.PropertyField(hapticsProp, new GUIContent("Crown Haptics Enabled", "Enables haptic detent clicks during rotation."));

            var resProp = serializedObject.FindProperty("m_CrownTickResolution");
            resProp.intValue = EditorGUILayout.IntSlider(new GUIContent("Crown Tick Resolution", "Crown haptic detents per crown range. Higher = finer steps."), resProp.intValue, 4, 60);
            EditorGUILayout.HelpBox("Crown haptic detents per crown range. Higher = finer steps.", MessageType.None);
        }

        private static void DrawSigningSection(SerializedObject serializedObject, WatchInputSettings settings)
        {
            EditorGUILayout.LabelField("Code Signing (Inherited from Player Settings)", EditorStyles.boldLabel);

            bool autoSign = PlayerSettings.iOS.appleEnableAutomaticSigning;
            string teamId = PlayerSettings.iOS.appleDeveloperTeamID;
            string maskedTeam = string.IsNullOrWhiteSpace(teamId)
                ? "None"
                : (teamId.Length > 3 ? teamId.Substring(0, 3) + "..." : teamId);

            if (autoSign)
            {
                EditorGUILayout.LabelField("Signing Status", $"Signing: Automatic (Team {maskedTeam})");
            }
            else
            {
                string profileDisplay = string.IsNullOrWhiteSpace(settings.WatchProvisioningProfileId) ? "None" : settings.WatchProvisioningProfileId;
                EditorGUILayout.LabelField("Signing Status", $"Signing: Manual — profile: {profileDisplay} (Team {maskedTeam})");

                var profileProp = serializedObject.FindProperty("m_WatchProvisioningProfileId");
                EditorGUILayout.PropertyField(profileProp, new GUIContent("Watch Provisioning Profile", "UUID or Specifier name for the watch app provisioning profile."));
            }

            var signingIssues = new List<ValidationIssue>();
            WatchInputValidator.ValidateSigning(settings, signingIssues);
            DrawIssues(signingIssues);
        }

        private static void DrawIssues(List<ValidationIssue> issues)
        {
            foreach (var issue in issues)
            {
                MessageType msgType = MessageType.Info;
                if (issue.Severity == ValidationSeverity.Warning) msgType = MessageType.Warning;
                else if (issue.Severity == ValidationSeverity.Error) msgType = MessageType.Error;

                EditorGUILayout.HelpBox($"{issue.Message} {issue.FixInstruction}", msgType);
            }
        }
    }
}
#endif
