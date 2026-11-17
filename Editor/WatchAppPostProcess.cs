#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace WatchInput.Editor
{
    /// <summary>
    /// Automates the creation and configuration of a modern watchOS companion application target
    /// inside the generated Xcode project during iOS builds.
    /// </summary>
    public static class WatchAppPostProcess
    {
        // Order 999 ensures this runs after standard Unity processing and other plugins have finished preparing the base Xcode project.
        [PostProcessBuild(999)]
        public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject)
        {
            if (buildTarget != BuildTarget.iOS)
            {
                return;
            }

            Debug.Log("[WatchInput] Starting watchOS companion target configuration in Xcode project...");

            string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var pbxProject = new PBXProject();
            pbxProject.ReadFromFile(pbxPath);

            string mainTargetGuid = pbxProject.GetUnityMainTargetGuid();
            string unityFrameworkGuid = pbxProject.GetUnityFrameworkTargetGuid();

            LinkWatchConnectivityFramework(pbxProject, unityFrameworkGuid);

            string watchTargetGuid = GetOrCreateWatchTarget(pbxProject, mainTargetGuid);
            ConfigureBuildProperties(pbxProject, watchTargetGuid);
            CopyAndLinkSwiftSources(pbxProject, watchTargetGuid, pathToBuiltProject);
            EnsureEmbedWatchContentPhase(pbxProject, pbxPath, mainTargetGuid, watchTargetGuid);

            pbxProject.WriteToFile(pbxPath);

            FixEmbedPhaseOrder(pbxPath);

            Debug.Log("[WatchInput] Successfully configured watchOS companion target.");
        }

        /// <summary>
        /// Links WatchConnectivity.framework to UnityFramework so native bridge code in iOS runtime can communicate with watchOS.
        /// </summary>
        private static void LinkWatchConnectivityFramework(PBXProject pbxProject, string unityFrameworkGuid)
        {
            if (!pbxProject.ContainsFramework(unityFrameworkGuid, "WatchConnectivity.framework"))
            {
                pbxProject.AddFrameworkToProject(unityFrameworkGuid, "WatchConnectivity.framework", false);
                Debug.Log("[WatchInput] Linked WatchConnectivity.framework to UnityFramework.");
            }
        }

        /// <summary>
        /// Retrieves an existing watch target or adds a new single-target watch application to maintain idempotency on rebuilds.
        /// </summary>
        private static string GetOrCreateWatchTarget(PBXProject pbxProject, string mainTargetGuid)
        {
            string watchTargetGuid = pbxProject.TargetGuidByName(WatchAppBuildConfig.WatchTargetName);
            bool isNewTarget = string.IsNullOrEmpty(watchTargetGuid);

            if (isNewTarget)
            {
                watchTargetGuid = pbxProject.AddTarget(
                    WatchAppBuildConfig.WatchTargetName,
                    ".app",
                    "com.apple.product-type.application"
                );

                pbxProject.AddSourcesBuildPhase(watchTargetGuid);
                pbxProject.AddResourcesBuildPhase(watchTargetGuid);
                pbxProject.AddFrameworksBuildPhase(watchTargetGuid);

                pbxProject.AddTargetDependency(mainTargetGuid, watchTargetGuid);
                Debug.Log($"[WatchInput] Created new watch application target: {WatchAppBuildConfig.WatchTargetName}");
            }
            else
            {
                Debug.Log($"[WatchInput] Found existing watch target '{WatchAppBuildConfig.WatchTargetName}'. Updating configuration.");
            }

            string watchBundleId = $"{PlayerSettings.applicationIdentifier}.watchkitapp";
            pbxProject.SetBuildProperty(watchTargetGuid, "PRODUCT_BUNDLE_IDENTIFIER", watchBundleId);

            return watchTargetGuid;
        }

        /// <summary>
        /// Configures Xcode compilation settings and Info.plist key-values required for modern independent watchOS SwiftUI apps.
        /// </summary>
        private static void ConfigureBuildProperties(PBXProject pbxProject, string watchTargetGuid)
        {
            // Directs Xcode to compile this target using watchOS SDK instead of iOS SDK.
            pbxProject.SetBuildProperty(watchTargetGuid, "SDKROOT", "watchos");

            // Enables compilation for both physical Apple Watch devices and Xcode simulators.
            pbxProject.SetBuildProperty(watchTargetGuid, "SUPPORTED_PLATFORMS", "watchsimulator watchos");

            // [TRAP FIX] '4' identifies Apple Watch family; prevents Xcode from defaulting to iPhone/iPad.
            pbxProject.SetBuildProperty(watchTargetGuid, "TARGETED_DEVICE_FAMILY", "4");

            // [TRAP FIX] Ensures asset catalogs and resources are built for the Apple Watch screen profile.
            pbxProject.SetBuildProperty(watchTargetGuid, "RESOURCES_TARGETED_DEVICE_FAMILY", "4");

            // Enforces minimum watchOS version supporting modern single-target SwiftUI App lifecycle.
            pbxProject.SetBuildProperty(watchTargetGuid, "WATCHOS_DEPLOYMENT_TARGET", WatchAppBuildConfig.WatchOSDeploymentTarget);

            // Employs standard watch architectures (e.g. arm64_32 and simulator equivalents).
            pbxProject.SetBuildProperty(watchTargetGuid, "ARCHS", "$(ARCHS_STANDARD)");
            pbxProject.SetBuildProperty(watchTargetGuid, "EXCLUDED_ARCHS", "");

            string teamId = PlayerSettings.iOS.appleDeveloperTeamID;
            if (string.IsNullOrEmpty(teamId))
            {
                Debug.LogWarning("[WatchInput] PlayerSettings.iOS.appleDeveloperTeamID is empty. Watch target code signing may require manual setup in Xcode.");
            }
            else
            {
                pbxProject.SetBuildProperty(watchTargetGuid, "DEVELOPMENT_TEAM", teamId);
            }

            // Instructs Xcode to manage code signing identities and provisioning profiles automatically.
            pbxProject.SetBuildProperty(watchTargetGuid, "CODE_SIGN_STYLE", "Automatic");

            pbxProject.SetBuildProperty(watchTargetGuid, "PRODUCT_NAME", WatchAppBuildConfig.WatchTargetName);

            // modern SwiftUI requires Swift 5 language mode.
            pbxProject.SetBuildProperty(watchTargetGuid, "SWIFT_VERSION", "5.0");

            // [TRAP FIX] Prevents standalone app archive output; ensures watch app is strictly embedded into host app.
            pbxProject.SetBuildProperty(watchTargetGuid, "SKIP_INSTALL", "YES");

            pbxProject.SetBuildProperty(watchTargetGuid, "ALWAYS_SEARCH_USER_PATHS", "NO");
            pbxProject.SetBuildProperty(watchTargetGuid, "CLANG_ENABLE_MODULES", "YES");

            // Keeps build and marketing version strings strictly aligned with host app for App Store submission.
            pbxProject.SetBuildProperty(watchTargetGuid, "CURRENT_PROJECT_VERSION", PlayerSettings.iOS.buildNumber);
            pbxProject.SetBuildProperty(watchTargetGuid, "MARKETING_VERSION", PlayerSettings.bundleVersion);

            // [TRAP FIX] Eliminates physical Info.plist dependency using Xcode 13+ build setting generation.
            pbxProject.SetBuildProperty(watchTargetGuid, "GENERATE_INFOPLIST_FILE", "YES");

            // [TRAP FIX] Designates modern single-target watchOS app model without legacy extension target.
            pbxProject.SetBuildProperty(watchTargetGuid, "INFOPLIST_KEY_WKApplication", "YES");

            // [TRAP FIX] Couples watchOS app to companion iOS app identifier for installation and WatchConnectivity.
            pbxProject.SetBuildProperty(watchTargetGuid, "INFOPLIST_KEY_WKCompanionAppBundleIdentifier", PlayerSettings.applicationIdentifier);

            pbxProject.SetBuildProperty(watchTargetGuid, "INFOPLIST_KEY_CFBundleDisplayName", WatchAppBuildConfig.DisplayName);
        }

        /// <summary>
        /// Copies package Swift templates to Xcode build output folder and links them to the watch target using relative paths.
        /// </summary>
        /// <summary>
        /// Scans WatchApp~ for all *.swift files, copies them into the Xcode project, and links to the watch target.
        /// Alphabetical sort ensures deterministic project file output across builds.
        /// </summary>
        private static void CopyAndLinkSwiftSources(PBXProject pbxProject, string watchTargetGuid, string pathToBuiltProject)
        {
            string packageRoot = GetPackageRootPath();
            string watchSourcesDir = Path.Combine(packageRoot, "WatchApp~");

            if (!Directory.Exists(watchSourcesDir))
            {
                Debug.LogError($"[WatchInput] WatchApp~ source directory not found at: {watchSourcesDir}");
                return;
            }

            string destDir = Path.Combine(pathToBuiltProject, WatchAppBuildConfig.SubfolderName);
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            string[] swiftFiles = Directory.GetFiles(watchSourcesDir, "*.swift");
            Array.Sort(swiftFiles, StringComparer.Ordinal);

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

                string projectRelativePath = $"{WatchAppBuildConfig.SubfolderName}/{fileName}";
                string existingFileGuid = pbxProject.FindFileGuidByProjectPath(projectRelativePath);

                if (string.IsNullOrEmpty(existingFileGuid))
                {
                    string fileGuid = pbxProject.AddFile(projectRelativePath, projectRelativePath, PBXSourceTree.Source);
                    pbxProject.AddFileToBuild(watchTargetGuid, fileGuid);
                    Debug.Log($"[WatchInput] Added {fileName} to watch target build sources.");
                }
            }

            Debug.Log($"[WatchInput] Processed {swiftFiles.Length} Swift source file(s) from WatchApp~.");
        }

        /// <summary>
        /// Adds a Copy Files build phase to host iOS app that places the compiled watch application into $(CONTENTS_FOLDER_PATH)/Watch.
        /// </summary>
        private static void EnsureEmbedWatchContentPhase(PBXProject pbxProject, string pbxPath, string mainTargetGuid, string watchTargetGuid)
        {
            // Protect against duplicate phase insertion when building into existing Xcode folders.
            if (File.Exists(pbxPath) && File.ReadAllText(pbxPath).Contains("/* Embed Watch Content */"))
            {
                Debug.Log("[WatchInput] 'Embed Watch Content' phase already present in Xcode project.");
                return;
            }

            // SubfolderSpec 16 corresponds to the Products Directory destination in Xcode PBXCopyFilesBuildPhase.
            string embedPhaseGuid = pbxProject.AddCopyFilesBuildPhase(
                mainTargetGuid,
                "Embed Watch Content",
                "$(CONTENTS_FOLDER_PATH)/Watch",
                "16"
            );

            string watchProductFileGuid = pbxProject.GetTargetProductFileRef(watchTargetGuid);
            if (!string.IsNullOrEmpty(watchProductFileGuid))
            {
                pbxProject.AddFileToBuildSection(mainTargetGuid, embedPhaseGuid, watchProductFileGuid);
                Debug.Log("[WatchInput] Added watch application product reference to 'Embed Watch Content' phase.");
            }
            else
            {
                Debug.LogError("[WatchInput] Failed to resolve target product file reference for watch target.");
            }
        }

        /// <summary>
        /// Moves 'Embed Watch Content' phase before 'Unity Phony Postprocess Target' so host app archives properly package the watch app.
        /// </summary>
        private static bool FixEmbedPhaseOrder(string pbxPath)
        {
            string[] lines = File.ReadAllLines(pbxPath);
            int buildPhasesStart = -1;
            int buildPhasesEnd = -1;
            bool foundTargetBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("buildPhases = ("))
                {
                    buildPhasesStart = i;
                }

                if (buildPhasesStart != -1 && lines[i].Contains("/* Embed Watch Content */,"))
                {
                    foundTargetBlock = true;
                }

                if (buildPhasesStart != -1 && lines[i].Contains(");"))
                {
                    if (foundTargetBlock)
                    {
                        buildPhasesEnd = i;
                        break;
                    }
                    buildPhasesStart = -1;
                }
            }

            if (!foundTargetBlock || buildPhasesStart == -1 || buildPhasesEnd == -1)
            {
                Debug.LogWarning("[WatchInput] FixEmbedPhaseOrder could not find buildPhases section containing '/* Embed Watch Content */,'.");
                return false;
            }

            int embedWatchIndex = -1;
            int phonyTargetIndex = -1;
            string embedWatchLine = null;

            for (int i = buildPhasesStart; i <= buildPhasesEnd; i++)
            {
                if (lines[i].Contains("/* Embed Watch Content */,"))
                {
                    embedWatchIndex = i;
                    embedWatchLine = lines[i];
                }
                else if (phonyTargetIndex == -1 && lines[i].Contains("/* Unity Phony Postprocess Target */,"))
                {
                    phonyTargetIndex = i;
                }
            }

            if (embedWatchIndex == -1)
            {
                Debug.LogWarning("[WatchInput] FixEmbedPhaseOrder could not find pattern '/* Embed Watch Content */,' inside buildPhases block.");
                return false;
            }

            if (phonyTargetIndex == -1)
            {
                Debug.LogWarning("[WatchInput] FixEmbedPhaseOrder could not find pattern '/* Unity Phony Postprocess Target */,' inside buildPhases block.");
                return false;
            }

            if (embedWatchIndex < phonyTargetIndex)
            {
                Debug.Log("[WatchInput] 'Embed Watch Content' phase is already positioned before 'Unity Phony Postprocess Target'. No reordering necessary.");
                return true;
            }

            var reorderedLines = new List<string>(lines);
            reorderedLines.RemoveAt(embedWatchIndex);

            phonyTargetIndex = reorderedLines.FindIndex(
                buildPhasesStart,
                buildPhasesEnd - buildPhasesStart,
                line => line.Contains("/* Unity Phony Postprocess Target */,")
            );

            if (phonyTargetIndex != -1)
            {
                reorderedLines.Insert(phonyTargetIndex, embedWatchLine);
                File.WriteAllLines(pbxPath, reorderedLines.ToArray());
                Debug.Log("[WatchInput] Successfully reordered 'Embed Watch Content' phase ahead of 'Unity Phony Postprocess Target'.");
                return true;
            }

            Debug.LogWarning("[WatchInput] FixEmbedPhaseOrder failed to determine insertion index for 'Unity Phony Postprocess Target' after removal.");
            return false;
        }

        /// <summary>
        /// Locates the package root directory dynamically without hardcoded absolute paths using PackageInfo reflection.
        /// </summary>
        private static string GetPackageRootPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WatchAppPostProcess).Assembly);
            if (packageInfo != null)
            {
                return packageInfo.resolvedPath;
            }

            // Fallback for direct local development folder structures
            string fallbackPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Debug.LogWarning($"[WatchInput] PackageInfo not found for assembly; using project root fallback: {fallbackPath}");
            return fallbackPath;
        }
    }
}
#endif
