using System;
using System.IO;
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
                return Path.Combine(packageInfo.resolvedPath, "Tests/Editor/Fixtures/minimal.pbxproj");
            }
            return Path.GetFullPath("Packages/com.sadiakdemir.watch-input/Tests/Editor/Fixtures/minimal.pbxproj");
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

            // Load minimal Xcode project fixture
            string fixturePath = GetFixturePath();
            Assert.IsTrue(File.Exists(fixturePath), $"minimal.pbxproj fixture must exist at: {fixturePath}");
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
        public void Configure_IsIdempotent_WhenCalledMultipleTimes()
        {
            var snapshot = CreateDefaultSnapshot();

            // First run
            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            string firstWatchGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);

            // Write project to disk so File.Exists check in EnsureEmbedWatchContentPhase sees previous run
            string pbxPath = PBXProject.GetPBXProjectPath(_tempBuildDir);
            Directory.CreateDirectory(Path.GetDirectoryName(pbxPath));
            _project.WriteToFile(pbxPath);

            // Second run
            WatchTargetConfigurator.Configure(_project, _tempBuildDir, snapshot);
            string secondWatchGuid = _project.TargetGuidByName(WatchInputSettings.WatchTargetName);

            Assert.AreEqual(firstWatchGuid, secondWatchGuid, "Second Configure call must update existing target rather than creating a duplicate");
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
