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
    /// Acts as the thin entry point delegating to pure WatchTargetConfigurator.
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

            string packageRoot = GetPackageRootPath();
            var snapshot = WatchBuildSnapshot.Create(WatchInputSettings.instance, packageRoot);

            WatchTargetConfigurator.Configure(pbxProject, pathToBuiltProject, snapshot);

            pbxProject.WriteToFile(pbxPath);

            WatchTargetConfigurator.FixEmbedPhaseOrder(pbxPath);

            Debug.Log("[WatchInput] Successfully configured watchOS companion target.");
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

            Debug.LogError("[WatchInput] PackageInfo could not be found for assembly.");
            return null;
        }
    }
}
#endif
