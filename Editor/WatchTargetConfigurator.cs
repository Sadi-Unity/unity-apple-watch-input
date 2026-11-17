#if UNITY_EDITOR && UNITY_IOS
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace WatchInput.Editor
{
    /// <summary>
    /// Configurator responsible for assembling the watchOS companion target in an Xcode project.
    /// Operates on memory PBXProject instances and WatchBuildSnapshot configurations without PlayerSettings side effects;
    /// performs file IO to write generated Swift sources, build configuration, and app icons into the build directory.
    /// </summary>
    internal static class WatchTargetConfigurator
    {
        public static void Configure(PBXProject pbxProject, string buildPath, WatchBuildSnapshot cfg)
        {
            string mainTargetGuid = pbxProject.GetUnityMainTargetGuid();
            string unityFrameworkGuid = pbxProject.GetUnityFrameworkTargetGuid();

            LinkWatchConnectivityFramework(pbxProject, unityFrameworkGuid);

            string watchTargetGuid = GetOrCreateWatchTarget(pbxProject, mainTargetGuid, cfg);
            ConfigureBuildProperties(pbxProject, watchTargetGuid, cfg);
            CopyAndLinkSwiftSources(pbxProject, watchTargetGuid, buildPath, cfg);
            ConfigureAppIcon(pbxProject, watchTargetGuid, buildPath, cfg);
            EnsureEmbedWatchContentPhase(pbxProject, buildPath, mainTargetGuid, watchTargetGuid);
        }

        public static void LinkWatchConnectivityFramework(PBXProject pbxProject, string unityFrameworkGuid)
        {
            if (!pbxProject.ContainsFramework(unityFrameworkGuid, "WatchConnectivity.framework"))
            {
                pbxProject.AddFrameworkToProject(unityFrameworkGuid, "WatchConnectivity.framework", false);
                Debug.Log("[WatchInput] Linked WatchConnectivity.framework to UnityFramework.");
            }
        }

        public static string GetOrCreateWatchTarget(PBXProject pbxProject, string mainTargetGuid, WatchBuildSnapshot cfg)
        {
            string watchTargetGuid = pbxProject.TargetGuidByName(WatchInputSettings.WatchTargetName);
            bool isNewTarget = string.IsNullOrEmpty(watchTargetGuid);

            if (isNewTarget)
            {
                watchTargetGuid = pbxProject.AddTarget(
                    WatchInputSettings.WatchTargetName,
                    ".app",
                    "com.apple.product-type.application"
                );

                pbxProject.AddSourcesBuildPhase(watchTargetGuid);
                pbxProject.AddResourcesBuildPhase(watchTargetGuid);
                pbxProject.AddFrameworksBuildPhase(watchTargetGuid);

                pbxProject.AddTargetDependency(mainTargetGuid, watchTargetGuid);
                Debug.Log($"[WatchInput] Created new watch application target: {WatchInputSettings.WatchTargetName}");
            }
            else
            {
                Debug.Log($"[WatchInput] Found existing watch target '{WatchInputSettings.WatchTargetName}'. Updating configuration.");
            }

            string watchBundleId = $"{cfg.ApplicationIdentifier}.{cfg.BundleIdSuffix}";
            pbxProject.SetBuildProperty(watchTargetGuid, "PRODUCT_BUNDLE_IDENTIFIER", watchBundleId);

            return watchTargetGuid;
        }

        public static void ConfigureBuildProperties(PBXProject pbxProject, string watchTargetGuid, WatchBuildSnapshot cfg)
        {
            // Directs Xcode to compile this target using watchOS SDK instead of iOS SDK.
            pbxProject.SetBuildProperty(watchTargetGuid, "SDKROOT", "watchos");

            // Enables compilation for both physical Apple Watch devices and Xcode simulators.
            pbxProject.SetBuildProperty(watchTargetGuid, "SUPPORTED_PLATFORMS", "watchsimulator watchos");

            // '4' identifies Apple Watch family; prevents Xcode from defaulting to iPhone/iPad.
            pbxProject.SetBuildProperty(watchTargetGuid, "TARGETED_DEVICE_FAMILY", "4");

            // Ensures asset catalogs and resources are built for the Apple Watch screen profile.
            pbxProject.SetBuildProperty(watchTargetGuid, "RESOURCES_TARGETED_DEVICE_FAMILY", "4");

            // Enforces minimum watchOS version supporting modern single-target SwiftUI App lifecycle.
            pbxProject.SetBuildProperty(watchTargetGuid, "WATCHOS_DEPLOYMENT_TARGET", cfg.MinimumWatchOS);

            // Employs standard watch architectures (e.g. arm64_32 and simulator equivalents).
            pbxProject.SetBuildProperty(watchTargetGuid, "ARCHS", "$(ARCHS_STANDARD)");
            pbxProject.SetBuildProperty(watchTargetGuid, "EXCLUDED_ARCHS", "");

            string teamId = cfg.AppleDeveloperTeamId;
            if (string.IsNullOrEmpty(teamId))
            {
                Debug.LogWarning("[WatchInput] PlayerSettings.iOS.appleDeveloperTeamID is empty. Watch target code signing may require manual setup in Xcode.");
            }
            else
            {
                pbxProject.SetBuildProperty(watchTargetGuid, "DEVELOPMENT_TEAM", teamId);
            }

            // Inherit signing configuration from PlayerSettings without modifying host iOS application settings.
            if (cfg.AppleEnableAutomaticSigning)
            {
                pbxProject.SetBuildProperty(watchTargetGuid, "CODE_SIGN_STYLE", "Automatic");
                pbxProject.SetBuildProperty(watchTargetGuid, "PROVISIONING_PROFILE_SPECIFIER", "");
            }
            else
            {
                pbxProject.SetBuildProperty(watchTargetGuid, "CODE_SIGN_STYLE", "Manual");
                if (!string.IsNullOrEmpty(cfg.WatchProvisioningProfileId))
                {
                    pbxProject.SetBuildProperty(watchTargetGuid, "PROVISIONING_PROFILE_SPECIFIER", cfg.WatchProvisioningProfileId);
                }
                else
                {
                    Debug.LogWarning("[WatchInput] Manual code signing is active in PlayerSettings, but WatchProvisioningProfileId is empty in WatchInputSettings.");
                }
            }

            pbxProject.SetBuildProperty(watchTargetGuid, "PRODUCT_NAME", WatchInputSettings.WatchTargetName);

            // Modern SwiftUI requires Swift 5 language mode.
            pbxProject.SetBuildProperty(watchTargetGuid, "SWIFT_VERSION", "5.0");

            // Prevents standalone app archive output; ensures watch app is strictly embedded into host app.
            pbxProject.SetBuildProperty(watchTargetGuid, "SKIP_INSTALL", "YES");

            pbxProject.SetBuildProperty(watchTargetGuid, "ALWAYS_SEARCH_USER_PATHS", "NO");
            pbxProject.SetBuildProperty(watchTargetGuid, "CLANG_ENABLE_MODULES", "YES");

            // Keeps build and marketing version strings strictly aligned with host app for App Store submission.
            pbxProject.SetBuildProperty(watchTargetGuid, "CURRENT_PROJECT_VERSION", cfg.BuildNumber);
            pbxProject.SetBuildProperty(watchTargetGuid, "MARKETING_VERSION", cfg.BundleVersion);

            // Eliminates physical Info.plist dependency using Xcode 13+ build setting generation.
            pbxProject.SetBuildProperty(watchTargetGuid, "GENERATE_INFOPLIST_FILE", "YES");

            // Designates modern single-target watchOS app model without legacy extension target.
            pbxProject.SetBuildProperty(watchTargetGuid, "INFOPLIST_KEY_WKApplication", "YES");

            // Couples watchOS app to companion iOS app identifier for installation and WatchConnectivity.
            pbxProject.SetBuildProperty(watchTargetGuid, "INFOPLIST_KEY_WKCompanionAppBundleIdentifier", cfg.ApplicationIdentifier);

            string displayName = string.IsNullOrEmpty(cfg.WatchAppDisplayName) ? cfg.ProductName : cfg.WatchAppDisplayName;
            pbxProject.SetBuildProperty(watchTargetGuid, "INFOPLIST_KEY_CFBundleDisplayName", displayName);
        }

        public static void CopyAndLinkSwiftSources(PBXProject pbxProject, string watchTargetGuid, string pathToBuiltProject, WatchBuildSnapshot cfg)
        {
            if (string.IsNullOrEmpty(cfg.WatchSourcesDirectory))
            {
                return;
            }

            string watchSourcesDir = cfg.WatchSourcesDirectory;
            if (!Directory.Exists(watchSourcesDir))
            {
                Debug.LogError($"[WatchInput] WatchApp~ source directory not found at: {watchSourcesDir}");
                return;
            }

            string destDir = Path.Combine(pathToBuiltProject, WatchInputSettings.SubfolderName);
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            string[] swiftFiles = Directory.GetFiles(watchSourcesDir, "*.swift");
            System.Array.Sort(swiftFiles, System.StringComparer.Ordinal);

            if (swiftFiles.Length == 0)
            {
                Debug.LogWarning("[WatchInput] No .swift files found in WatchApp~ directory.");
                return;
            }

            foreach (string sourceFilePath in swiftFiles)
            {
                string fileName = Path.GetFileName(sourceFilePath);
                string destFilePath = Path.Combine(destDir, fileName);
                File.Copy(sourceFilePath, destFilePath, true);

                string projectRelativePath = $"{WatchInputSettings.SubfolderName}/{fileName}";
                string existingFileGuid = pbxProject.FindFileGuidByProjectPath(projectRelativePath);

                if (string.IsNullOrEmpty(existingFileGuid))
                {
                    string fileGuid = pbxProject.AddFile(projectRelativePath, projectRelativePath, PBXSourceTree.Source);
                    pbxProject.AddFileToBuild(watchTargetGuid, fileGuid);
                    Debug.Log($"[WatchInput] Added {fileName} to watch target build sources.");
                }
            }

            // Overwrite WatchInputBuildConfig.swift with current settings values
            WriteBuildConfigSwift(destDir, cfg);

            Debug.Log($"[WatchInput] Processed {swiftFiles.Length} Swift source file(s) from WatchApp~.");
        }

        public static void WriteBuildConfigSwift(string destDir, WatchBuildSnapshot cfg)
        {
            string ticksFormatted = cfg.CrownTickResolution.ToString("0.0", CultureInfo.InvariantCulture);
            string hapticsFormatted = cfg.CrownHapticsEnabled ? "true" : "false";

            string content = $@"import Foundation

// Automatically generated by WatchInput Unity PostProcessBuild.
// Do not edit manually — changes will be overwritten on build.
enum WatchInputBuildConfig {{
    static let crownHapticsEnabled: Bool = {hapticsFormatted}
    static let ticksPerRange: Double = {ticksFormatted}
}}
";
            string targetPath = Path.Combine(destDir, "WatchInputBuildConfig.swift");
            File.WriteAllText(targetPath, content);
            Debug.Log("[WatchInput] Overwrote WatchInputBuildConfig.swift with current build settings.");
        }

        public static void ConfigureAppIcon(PBXProject pbxProject, string watchTargetGuid, string pathToBuiltProject, WatchBuildSnapshot cfg)
        {
            if (string.IsNullOrEmpty(cfg.AppIconSourcePath))
            {
                Debug.LogWarning("[WatchInput] No watch app icon assigned. App Store submission requires an icon.");
                return;
            }

            string sourceIconPath = cfg.AppIconSourcePath;
            if (!File.Exists(sourceIconPath))
            {
                Debug.LogWarning("[WatchInput] Could not resolve file path for assigned app icon.");
                return;
            }

            string destFolder = Path.Combine(pathToBuiltProject, WatchInputSettings.SubfolderName);
            string xcassetsDir = Path.Combine(destFolder, "Assets.xcassets");
            string iconSetDir = Path.Combine(xcassetsDir, "AppIcon.appiconset");
            if (!Directory.Exists(iconSetDir))
            {
                Directory.CreateDirectory(iconSetDir);
            }

            string destIconPath = Path.Combine(iconSetDir, "AppIcon-1024.png");
            File.Copy(sourceIconPath, destIconPath, true);

            string contentsJson = @"{
  ""images"" : [
    {
      ""filename"" : ""AppIcon-1024.png"",
      ""idiom"" : ""universal"",
      ""platform"" : ""watchos"",
      ""size"" : ""1024x1024""
    }
  ],
  ""info"" : {
    ""author"" : ""xcode"",
    ""version"" : 1
  }
}
";
            File.WriteAllText(Path.Combine(iconSetDir, "Contents.json"), contentsJson);

            string projectRelativePath = $"{WatchInputSettings.SubfolderName}/Assets.xcassets";
            string existingFileGuid = pbxProject.FindFileGuidByProjectPath(projectRelativePath);

            if (string.IsNullOrEmpty(existingFileGuid))
            {
                string fileGuid = pbxProject.AddFile(projectRelativePath, projectRelativePath, PBXSourceTree.Source);
                pbxProject.AddFileToBuild(watchTargetGuid, fileGuid);
                Debug.Log("[WatchInput] Added Assets.xcassets to watch target resources.");
            }

            pbxProject.SetBuildProperty(watchTargetGuid, "ASSETCATALOG_COMPILER_APPICON_NAME", "AppIcon");
        }

        public static void EnsureEmbedWatchContentPhase(PBXProject pbxProject, string pathToBuiltProject, string mainTargetGuid, string watchTargetGuid)
        {
            // SubfolderSpec 16 corresponds to the Products Directory destination in Xcode PBXCopyFilesBuildPhase.
            // AddCopyFilesBuildPhaseBeforeTargetPostprocess positions the phase before Unity's postprocess placeholder,
            // returning the existing phase GUID if one with the same name, dstPath, and spec already exists (idempotent).
            string embedPhaseGuid = pbxProject.AddCopyFilesBuildPhaseBeforeTargetPostprocess(
                mainTargetGuid,
                "Embed Watch Content",
                "$(CONTENTS_FOLDER_PATH)/Watch",
                "16"
            );

            string watchProductFileGuid = pbxProject.GetTargetProductFileRef(watchTargetGuid);
            if (!string.IsNullOrEmpty(watchProductFileGuid))
            {
                if (!IsProductFileInPhase(pbxProject, mainTargetGuid, watchProductFileGuid))
                {
                    pbxProject.AddFileToBuildSection(mainTargetGuid, embedPhaseGuid, watchProductFileGuid);
                    Debug.Log("[WatchInput] Added watch application product reference to 'Embed Watch Content' phase.");
                }
                else
                {
                    Debug.Log("[WatchInput] Watch application product reference already present in 'Embed Watch Content' phase.");
                }
            }
            else
            {
                Debug.LogError("[WatchInput] Failed to resolve target product file reference for watch target.");
            }
        }

        private static bool IsProductFileInPhase(PBXProject pbxProject, string targetGuid, string fileGuid)
        {
            var method = typeof(PBXProject).GetMethod("BuildFilesGetForSourceFile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (method != null)
            {
                return method.Invoke(pbxProject, new object[] { targetGuid, fileGuid }) != null;
            }

            return pbxProject.WriteToString().Contains($"{WatchInputSettings.WatchTargetName}.app in Embed Watch Content");
        }
    }
}
#endif
