#if UNITY_IOS
using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.iOS.Xcode;
using UnityEngine;
using WatchInput.Editor;

namespace WatchInput.Tests.Editor
{
    [TestFixture]
    public class WatchTargetConfiguratorTests
    {
        private string _tempBuildDir;
        private string _tempSourcesDir;
        private string _tempIconPath;
        private PBXProject _project;
        private string _mainTargetGuid;
        private string _frameworkTargetGuid;
        private string _fixtureContent;

        private static string GetFixturePath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WatchTargetConfiguratorTests).Assembly);
            if (packageInfo != null)
            {
                return Path.Combine(packageInfo.resolvedPath, "Tests/Editor/Fixtures/unity-generated.pbxproj");
            }
            return Path.GetFullPath("Packages/com.sadiakdemir.watch-input/Tests/Editor/Fixtures/unity-generated.pbxproj");
        }

        [SetUp]
        public void SetUp()
        {
            _tempBuildDir = Path.Combine(Path.GetTempPath(), "WatchInput_Build_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempBuildDir);

            _tempSourcesDir = Path.Combine(Path.GetTempPath(), "WatchInput_SwiftSources_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempSourcesDir);

            // Create dummy swift source files
            File.WriteAllText(Path.Combine(_tempSourcesDir, "B.swift"), "// B file content");
            File.WriteAllText(Path.Combine(_tempSourcesDir, "A.swift"), "// A file content");

            // Create dummy icon file
            _tempIconPath = Path.Combine(_tempSourcesDir, "TestIcon-1024.png");
            File.WriteAllBytes(_tempIconPath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            // Load generated Xcode project fixture
            string fixturePath = GetFixturePath();
            Assert.IsTrue(File.Exists(fixturePath), $"unity-generated.pbxproj fixture must exist at: {fixturePath}");
            _fixtureContent = File.ReadAllText(fixturePath);

            _project = new PBXProject();
            _project.ReadFromString(_fixtureContent);

            _mainTargetGuid = _project.GetUnityMainTargetGuid();
            _frameworkTargetGuid = _project.GetUnityFrameworkTargetGuid();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempBuildDir))
            {
                Directory.Delete(_tempBuildDir, true);
            }

            if (Directory.Exists(_tempSourcesDir))
            {
                Directory.Delete(_tempSourcesDir, true);
            }
        }

        private WatchBuildSnapshot CreateDefaultSnapshot(bool autoSigning = true, bool withIcon = false)
        {
            return new WatchBuildSnapshot
            {
                ApplicationIdentifier = "com.example.testgame",
                ProductName = "TestGame",
                BundleVersion = "1.0.0",
                BuildNumber = "12",
                AppleDeveloperTeamId = "TEAMID123",
                AppleEnableAutomaticSigning = autoSigning,

                WatchAppDisplayName = "WatchGame",
                BundleIdSuffix = "watchkitapp",
                MinimumWatchOS = "9.0",
                CrownHapticsEnabled = true,
                CrownTickResolution = 24,
                WatchProvisioningProfileId = "PROFILE-UUID-1234",
                AppIconSourcePath = withIcon ? _tempIconPath : null,
                WatchSourcesDirectory = _tempSourcesDir
            };
        }

        [Test]
        public void Fixture_TargetsCanBeResolved()
        {
            Assert.IsFalse(string.IsNullOrEmpty(_mainTargetGuid), "Unity-iPhone target must be resolved from fixture");
            Assert.IsFalse(string.IsNullOrEmpty(_frameworkTargetGuid), "UnityFramework target must be resolved from fixture");
        }

        [Test]
        public void Configure_CreatesWatchTarget_WhenCalledFirstTime()
        {
            var snapshot = CreateDefaultSnapshot();

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);

            string watchTargetGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);
            Assert.IsFalse(string.IsNullOrEmpty(watchTargetGuid), "Watch companion target must be created");
        }

        [Test]
        public void Configure_EmbedWatchContentPhase_PrecedesUnityPhonyPostprocessTarget()
        {
            var snapshot = CreateDefaultSnapshot();

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);

            string[] phases = _project.GetAllBuildPhasesForTarget(_mainTargetGuid);
            int embedIndex = Array.FindIndex(phases, p => _project.GetBuildPhaseName(p) == "Embed Watch Content");
            int phonyIndex = Array.FindIndex(phases, p => _project.GetBuildPhaseName(p) == "Unity Phony Postprocess Target");

            Assert.IsTrue(embedIndex >= 0, "'Embed Watch Content' phase must exist in target buildPhases");
            Assert.IsTrue(phonyIndex >= 0, "'Unity Phony Postprocess Target' phase must exist in target buildPhases");
            Assert.IsTrue(embedIndex < phonyIndex, $"'Embed Watch Content' (index {embedIndex}) must precede 'Unity Phony Postprocess Target' (index {phonyIndex}) in buildPhases order");
        }

        [Test]
        public void Configure_IsIdempotent_WhenCalledMultipleTimes()
        {
            var snapshot = CreateDefaultSnapshot();

            // First run
            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            string firstWatchGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);

            // Second run directly on the same in-memory PBXProject
            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            string secondWatchGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);

            Assert.AreEqual(firstWatchGuid, secondWatchGuid, "Second Configure call must update existing target rather than creating a duplicate");

            // Explicitly count all 5 configured structures in the project output
            string pbxText = _project.WriteToString();

            // 1. Watch target count
            int nativeTargetsStart = pbxText.IndexOf("/* Begin PBXNativeTarget section */", StringComparison.Ordinal);
            int nativeTargetsEnd = pbxText.IndexOf("/* End PBXNativeTarget section */", StringComparison.Ordinal);
            string nativeTargetsSection = pbxText.Substring(nativeTargetsStart, nativeTargetsEnd - nativeTargetsStart);
            int watchTargetCount = Regex.Matches(nativeTargetsSection, @"name = ""?WatchInput Watch App""?;").Count;
            Assert.AreEqual(1, watchTargetCount, "Watch target count must be 1");

            // 2. 'Embed Watch Content' phase count
            int copyFilesStart = pbxText.IndexOf("/* Begin PBXCopyFilesBuildPhase section */", StringComparison.Ordinal);
            int copyFilesEnd = pbxText.IndexOf("/* End PBXCopyFilesBuildPhase section */", StringComparison.Ordinal);
            string copyFilesSection = pbxText.Substring(copyFilesStart, copyFilesEnd - copyFilesStart);
            int embedPhaseCount = Regex.Matches(copyFilesSection, @"/\* Embed Watch Content \*/ = \{").Count;
            Assert.AreEqual(1, embedPhaseCount, "'Embed Watch Content' build phase count must be 1");

            // 3. Product file in embed phase count
            int prodFileInPhaseCount = Regex.Matches(copyFilesSection, @"/\* WatchInput Watch App\.app in Embed Watch Content \*/").Count;
            Assert.AreEqual(1, prodFileInPhaseCount, "Product file in 'Embed Watch Content' phase count must be 1");

            // 4. Target dependency count
            int targetDepStart = pbxText.IndexOf("/* Begin PBXTargetDependency section */", StringComparison.Ordinal);
            int targetDepEnd = pbxText.IndexOf("/* End PBXTargetDependency section */", StringComparison.Ordinal);
            string targetDepSection = targetDepStart >= 0 ? pbxText.Substring(targetDepStart, targetDepEnd - targetDepStart) : "";
            int targetDepCount = Regex.Matches(targetDepSection, @"/\* WatchInput Watch App \*/;").Count;
            Assert.AreEqual(1, targetDepCount, "Target dependency count must be 1");

            // 5. WatchConnectivity link count
            int frameworksStart = pbxText.IndexOf("/* Begin PBXFrameworksBuildPhase section */", StringComparison.Ordinal);
            int frameworksEnd = pbxText.IndexOf("/* End PBXFrameworksBuildPhase section */", StringComparison.Ordinal);
            string frameworksSection = pbxText.Substring(frameworksStart, frameworksEnd - frameworksStart);
            int wcLinkCount = Regex.Matches(frameworksSection, @"WatchConnectivity\.framework in Frameworks").Count;
            Assert.AreEqual(1, wcLinkCount, "WatchConnectivity.framework link count in Frameworks build phase must be 1");
        }

        [Test]
        public void Configure_SetsRequiredBuildProperties()
        {
            var snapshot = CreateDefaultSnapshot();

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            string watchGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);

            Assert.AreEqual("watchos", _project.GetBuildPropertyForAnyConfig(watchGuid, "SDKROOT"));
            Assert.AreEqual("4", _project.GetBuildPropertyForAnyConfig(watchGuid, "TARGETED_DEVICE_FAMILY"));
            Assert.AreEqual("4", _project.GetBuildPropertyForAnyConfig(watchGuid, "RESOURCES_TARGETED_DEVICE_FAMILY"));
            Assert.AreEqual("YES", _project.GetBuildPropertyForAnyConfig(watchGuid, "SKIP_INSTALL"));
            Assert.AreEqual("YES", _project.GetBuildPropertyForAnyConfig(watchGuid, "GENERATE_INFOPLIST_FILE"));
            Assert.AreEqual("YES", _project.GetBuildPropertyForAnyConfig(watchGuid, "INFOPLIST_KEY_WKApplication"));
            Assert.AreEqual("5.0", _project.GetBuildPropertyForAnyConfig(watchGuid, "SWIFT_VERSION"));
            Assert.AreEqual("9.0", _project.GetBuildPropertyForAnyConfig(watchGuid, "WATCHOS_DEPLOYMENT_TARGET"));
        }

        [Test]
        public void Configure_SetsCompanionBundleIdentifier_AndProductBundleId()
        {
            var snapshot = CreateDefaultSnapshot();

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            string watchGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);

            string expectedBundleId = "com.example.testgame.watchkitapp";
            Assert.AreEqual(expectedBundleId, _project.GetBuildPropertyForAnyConfig(watchGuid, "PRODUCT_BUNDLE_IDENTIFIER"));
            Assert.AreEqual("com.example.testgame", _project.GetBuildPropertyForAnyConfig(watchGuid, "INFOPLIST_KEY_WKCompanionAppBundleIdentifier"));
            Assert.AreEqual("WatchGame", _project.GetBuildPropertyForAnyConfig(watchGuid, "INFOPLIST_KEY_CFBundleDisplayName"));
        }

        [Test]
        public void Configure_AutomaticSigning_SetsCodeSignStyleAndClearsProfile()
        {
            var snapshot = CreateDefaultSnapshot(autoSigning: true);

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            string watchGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);

            Assert.AreEqual("Automatic", _project.GetBuildPropertyForAnyConfig(watchGuid, "CODE_SIGN_STYLE"));
            Assert.AreEqual("", _project.GetBuildPropertyForAnyConfig(watchGuid, "PROVISIONING_PROFILE_SPECIFIER"));
            Assert.AreEqual("TEAMID123", _project.GetBuildPropertyForAnyConfig(watchGuid, "DEVELOPMENT_TEAM"));
        }

        [Test]
        public void Configure_ManualSigning_SetsCodeSignStyleAndProfileSpecifier()
        {
            var snapshot = CreateDefaultSnapshot(autoSigning: false);

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            string watchGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);

            Assert.AreEqual("Manual", _project.GetBuildPropertyForAnyConfig(watchGuid, "CODE_SIGN_STYLE"));
            Assert.AreEqual("PROFILE-UUID-1234", _project.GetBuildPropertyForAnyConfig(watchGuid, "PROVISIONING_PROFILE_SPECIFIER"));
        }

        [Test]
        public void Configure_LinksWatchConnectivityFramework_ToUnityFrameworkOnce()
        {
            var snapshot = CreateDefaultSnapshot();

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            Assert.IsTrue(_project.ContainsFramework(_frameworkTargetGuid, "WatchConnectivity.framework"));

            // Repeat to ensure idempotency
            WatchTargetConfigurator.LinkWatchConnectivityFramework(_project, _frameworkTargetGuid);
            Assert.IsTrue(_project.ContainsFramework(_frameworkTargetGuid, "WatchConnectivity.framework"));
        }

        [Test]
        public void Configure_GeneratesWatchInputBuildConfigSwift_WithMatchingValues()
        {
            var snapshot = CreateDefaultSnapshot();
            snapshot.CrownHapticsEnabled = false;
            snapshot.CrownTickResolution = 36;

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);

            string configPath = Path.Combine(_tempBuildDir, WatchInputSettings.SubfolderName, "WatchInputBuildConfig.swift");
            Assert.IsTrue(File.Exists(configPath), "WatchInputBuildConfig.swift must be created");

            string content = File.ReadAllText(configPath);
            Assert.IsTrue(content.Contains("crownHapticsEnabled: Bool = false"), "Config swift must reflect crownHapticsEnabled false");
            Assert.IsTrue(content.Contains("ticksPerRange: Double = 36.0"), "Config swift must reflect ticksPerRange 36.0");
        }

        [Test]
        public void Configure_CopiesSwiftSourcesInAlphabeticalOrder()
        {
            var snapshot = CreateDefaultSnapshot();

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);

            string destDir = Path.Combine(_tempBuildDir, WatchInputSettings.SubfolderName);
            Assert.IsTrue(File.Exists(Path.Combine(destDir, "A.swift")), "A.swift must be copied");
            Assert.IsTrue(File.Exists(Path.Combine(destDir, "B.swift")), "B.swift must be copied");

            string fileGuidA = _project.FindFileGuidByProjectPath($"{WatchInputSettings.SubfolderName}/A.swift");
            string fileGuidB = _project.FindFileGuidByProjectPath($"{WatchInputSettings.SubfolderName}/B.swift");

            Assert.IsFalse(string.IsNullOrEmpty(fileGuidA), "A.swift must be linked in PBXProject");
            Assert.IsFalse(string.IsNullOrEmpty(fileGuidB), "B.swift must be linked in PBXProject");
        }

        [Test]
        public void Configure_AppIcon_GeneratesXcassetsAndContentsJson()
        {
            var snapshot = CreateDefaultSnapshot(withIcon: true);

            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            string watchGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);

            string iconSetDir = Path.Combine(_tempBuildDir, WatchInputSettings.SubfolderName, "Assets.xcassets", "AppIcon.appiconset");
            string contentsJsonPath = Path.Combine(iconSetDir, "Contents.json");
            string destIconPath = Path.Combine(iconSetDir, "AppIcon-1024.png");

            Assert.IsTrue(File.Exists(contentsJsonPath), "Contents.json must be written");
            Assert.IsTrue(File.Exists(destIconPath), "AppIcon-1024.png must be copied");

            string jsonContent = File.ReadAllText(contentsJsonPath);
            Assert.IsTrue(jsonContent.Contains("\"platform\" : \"watchos\""), "Contents.json must target watchos platform");
            Assert.AreEqual("AppIcon", _project.GetBuildPropertyForAnyConfig(watchGuid, "ASSETCATALOG_COMPILER_APPICON_NAME"));
        }
    }
}
#endif
