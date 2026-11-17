#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WatchInput.Editor
{
    /// <summary>
    /// Executes pre-build diagnostic checks prior to iOS build generation.
    /// Does not abort the build; outputs single-line actionable warnings for signing, bundle ID, or icon issues.
    /// </summary>
    public class WatchInputPreflight : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            var settings = WatchInputSettings.instance;
            var issues = WatchInputValidator.Validate(settings);

            foreach (var issue in issues)
            {
                Debug.LogWarning($"[WatchInput] Preflight: {issue.Message} — {issue.FixInstruction}");
            }
        }
    }
}
#endif
