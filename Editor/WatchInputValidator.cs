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

            ValidateSigning(settings, issues);
            ValidateBundleSuffix(settings, issues);
            ValidateMinimumWatchOS(settings, issues);
            ValidateAppIcon(settings, issues);

            return issues;
        }

        public static void ValidateSigning(WatchInputSettings settings, List<ValidationIssue> issues)
        {
            string teamId = PlayerSettings.iOS.appleDeveloperTeamID;
            if (string.IsNullOrWhiteSpace(teamId))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "Apple Developer Team ID is not set.",
                    "Set your Team ID in Project Settings > Player > iOS > Other Settings > Identification > Signing Team ID."
                ));
            }

            bool autoSign = PlayerSettings.iOS.appleEnableAutomaticSigning;
            if (!autoSign)
            {
                if (string.IsNullOrWhiteSpace(settings.WatchProvisioningProfileId))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "Manual code signing is active but no Watch Provisioning Profile is specified.",
                        "Provide a Watch Provisioning Profile Specifier/UUID in Project Settings > Watch Input."
                    ));
                }
            }
        }

        public static void ValidateBundleSuffix(WatchInputSettings settings, List<ValidationIssue> issues)
        {
            string suffix = settings.BundleIdSuffix;
            if (string.IsNullOrEmpty(suffix) || !s_BundleSuffixRegex.IsMatch(suffix))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"Invalid bundle ID suffix '{suffix}'.",
                    "Bundle ID suffix must contain only alphanumeric characters and hyphens in Project Settings > Watch Input."
                ));
            }
        }

        public static void ValidateMinimumWatchOS(WatchInputSettings settings, List<ValidationIssue> issues)
        {
            string raw = settings.MinimumWatchOS;
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

        public static void ValidateAppIcon(WatchInputSettings settings, List<ValidationIssue> issues)
        {
            Texture2D icon = settings.AppIcon;
            if (icon == null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "No watch app icon assigned.",
                    "Assign a 1024x1024 PNG icon without alpha in Project Settings > Watch Input (Required for App Store submission)."
                ));
                return;
            }

            if (icon.width != 1024 || icon.height != 1024)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"Watch app icon must be 1024x1024 (current: {icon.width}x{icon.height}).",
                    "Provide a 1024x1024 icon in Project Settings > Watch Input."
                ));
            }

            string assetPath = AssetDatabase.GetAssetPath(icon);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "Watch app icon must be a PNG file.",
                    "Export icon as a PNG image in Project Settings > Watch Input."
                ));
            }
            else
            {
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
}
#endif
