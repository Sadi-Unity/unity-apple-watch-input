#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace WatchInput.Editor
{
    /// <summary>
    /// Automates the creation and configuration of a modern watchOS companion application target
    /// inside the generated Xcode project during iOS builds.
    /// Acts as the thin entry point delegating to WatchTargetConfigurator.
    /// </summary>
    public static class WatchAppPostProcess
    {
        // Order 999 requests late execution after standard Unity processing and other plugins have prepared the base Xcode project (request, not guaranteed).
        [PostProcessBuild(999)]
        public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject)
        {
            if (buildTarget != BuildTarget.iOS)
            {
                return;
            }

            Debug.Log("[WatchInput] Starting watchOS companion target configuration in Xcode project...");

            string packageRoot = GetPackageRootPath();
            if (string.IsNullOrEmpty(packageRoot))
            {
                Debug.LogError("[WatchInput] Watch target configuration aborted: package root could not be determined.");
                return;
            }

            string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var pbxProject = new PBXProject();
            pbxProject.ReadFromFile(pbxPath);

            var snapshot = WatchBuildSnapshot.Create(WatchInputSettings.instance, packageRoot);

            WatchTargetConfigurator.Configure(pbxProject, pathToBuiltProject, snapshot);

            pbxProject.WriteToFile(pbxPath);

            Debug.Log("[WatchInput] Successfully configured watchOS companion target.");
        }

        /// <summary>
        /// Locates the package root directory dynamically without hardcoded absolute paths using PackageInfo API.
        /// </summary>
        private static string GetPackageRootPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WatchAppPostProcess).Assembly);
            if (packageInfo != null)
            {
                return packageInfo.resolvedPath;
            }

            Debug.LogError("[WatchInput] PackageInfo could not be found for assembly.");
            return null;
        }
    }
}
#endif
