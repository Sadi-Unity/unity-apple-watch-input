using System.Collections.Generic;
using NUnit.Framework;
using WatchInput.Editor;

namespace WatchInput.Tests.Editor
{
    [TestFixture]
    public class WatchInputValidatorTests
    {
        [Test]
        [TestCase("watchkitapp")]
        [TestCase("watch-app-2")]
        [TestCase("Companion123")]
        public void ValidateBundleSuffix_ValidAlphanumericAndHyphen_ReportsNoError(string suffix)
        {
            var issues = new List<ValidationIssue>();

            WatchInputValidator.ValidateBundleSuffix(suffix, issues);

            Assert.AreEqual(0, issues.Count, $"Valid suffix '{suffix}' should produce no validation issues");
        }

        [Test]
        [TestCase("watch.app")]
        [TestCase("watch app")]
        [TestCase("watch@app")]
        [TestCase("")]
        public void ValidateBundleSuffix_InvalidCharsOrEmpty_ReportsError(string suffix)
        {
            var issues = new List<ValidationIssue>();

            WatchInputValidator.ValidateBundleSuffix(suffix, issues);

            Assert.IsTrue(issues.Exists(i => i.Severity == ValidationSeverity.Error),
                $"Invalid suffix '{suffix}' must produce a ValidationSeverity.Error");
        }

        [Test]
        [TestCase("9.0")]
        [TestCase("9.4")]
        [TestCase("10.0")]
        [TestCase("11.1")]
        public void ValidateMinimumWatchOS_ValidModernVersion_ReportsNoError(string version)
        {
            // Specifically verifies that "10.0" is not considered less than "9.0" via string sorting trap
            var issues = new List<ValidationIssue>();

            WatchInputValidator.ValidateMinimumWatchOS(version, issues);

            Assert.AreEqual(0, issues.Count, $"Modern supported version '{version}' should produce no issues");
        }

        [Test]
        [TestCase("8.0")]
        [TestCase("7.6")]
        public void ValidateMinimumWatchOS_BelowMinSupported_ReportsWarning(string version)
        {
            var issues = new List<ValidationIssue>();

            WatchInputValidator.ValidateMinimumWatchOS(version, issues);

            Assert.IsTrue(issues.Exists(i => i.Severity == ValidationSeverity.Warning),
                $"Legacy version '{version}' below 9.0 must produce a Warning");
        }

        [Test]
        [TestCase("abc")]
        [TestCase("v9.0")]
        [TestCase("")]
        public void ValidateMinimumWatchOS_InvalidFormat_ReportsError(string version)
        {
            var issues = new List<ValidationIssue>();

            WatchInputValidator.ValidateMinimumWatchOS(version, issues);

            Assert.IsTrue(issues.Exists(i => i.Severity == ValidationSeverity.Error),
                $"Unparseable version format '{version}' must produce an Error");
        }

        [Test]
        public void ValidateSigning_ManualSigningWithEmptyProfile_ReportsWarning()
        {
            var issues = new List<ValidationIssue>();

            WatchInputValidator.ValidateSigning(automatic: false, teamId: "TEAM12345", watchProfile: "", issues: issues);

            Assert.IsTrue(issues.Exists(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("Watch Provisioning Profile")),
                "Manual signing with empty Watch Provisioning Profile must report warning");
        }

        [Test]
        public void ValidateSigning_EmptyTeamId_ReportsWarning()
        {
            var issues = new List<ValidationIssue>();

            WatchInputValidator.ValidateSigning(automatic: true, teamId: "", watchProfile: "", issues: issues);

            Assert.IsTrue(issues.Exists(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("Team ID is not set")),
                "Empty Apple Developer Team ID must report warning");
        }
    }
}
