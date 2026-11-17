#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace WatchInput.Editor
{
    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public struct ValidationIssue
    {
        public ValidationSeverity Severity;
        public string Message;
        public string FixInstruction;

        public ValidationIssue(ValidationSeverity severity, string message, string fixInstruction)
        {
            Severity = severity;
            Message = message;
            FixInstruction = fixInstruction;
        }
    }

    public static class WatchInputValidator
    {
        private static readonly Regex s_BundleSuffixRegex = new Regex("^[a-zA-Z0-9-]+$", RegexOptions.Compiled);
        private static readonly Version s_MinSupportedWatchOS = new Version(9, 0);

        public static List<ValidationIssue> Validate(WatchInputSettings settings)
        {
            var issues = new List<ValidationIssue>();

            bool autoSign = PlayerSettings.iOS.appleEnableAutomaticSigning;
            string teamId = PlayerSettings.iOS.appleDeveloperTeamID;
            ValidateSigning(autoSign, teamId, settings.WatchProvisioningProfileId, issues);

            ValidateBundleSuffix(settings.BundleIdSuffix, issues);
            ValidateMinimumWatchOS(settings.MinimumWatchOS, issues);

            string assetPath = settings.AppIcon != null ? AssetDatabase.GetAssetPath(settings.AppIcon) : null;
            ValidateAppIcon(assetPath, issues);

            return issues;
        }

        public static void ValidateSigning(bool automatic, string teamId, string watchProfile, List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(teamId))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "Apple Developer Team ID is not set.",
                    "Set your Team ID in Project Settings > Player > iOS > Other Settings > Identification > Signing Team ID."
                ));
            }

            if (!automatic)
            {
                if (string.IsNullOrWhiteSpace(watchProfile))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "Manual code signing is active but no Watch Provisioning Profile is specified.",
                        "Provide a Watch Provisioning Profile Specifier/UUID in Project Settings > Watch Input."
                    ));
                }
            }
        }

        public static void ValidateBundleSuffix(string suffix, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(suffix) || !s_BundleSuffixRegex.IsMatch(suffix))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"Invalid bundle ID suffix '{suffix}'.",
                    "Bundle ID suffix must contain only alphanumeric characters and hyphens in Project Settings > Watch Input."
                ));
            }
        }

        public static void ValidateMinimumWatchOS(string raw, List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(raw) || !Version.TryParse(raw, out Version parsedVersion))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"Minimum watchOS version '{raw}' could not be parsed.",
                    "Enter a valid version format like '9.0' or '10.0' in Project Settings > Watch Input."
                ));
            }
            else if (parsedVersion < s_MinSupportedWatchOS)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"Minimum watchOS version is set below 9.0 ({raw}).",
                    "Set minimum watchOS version to 9.0 or higher in Project Settings > Watch Input."
                ));
            }
        }

        public static void ValidateAppIcon(string assetPath, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "No watch app icon assigned.",
                    "Assign a 1024x1024 PNG icon without alpha in Project Settings > Watch Input (Required for App Store submission)."
                ));
                return;
            }

            if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "Watch app icon must be a PNG file.",
                    "Export icon as a PNG image in Project Settings > Watch Input."
                ));
                return;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null && importer.DoesSourceTextureHaveAlpha())
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "Watch app icon contains an alpha channel (Apple App Store forbids transparency in app icons).",
                    "Export icon as a 24-bit RGB PNG without transparency (Apple App Store requirement)."
                ));
            }
        }
    }
}
#endif
