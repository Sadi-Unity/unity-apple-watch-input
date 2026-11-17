using System;
using System.IO;
using NUnit.Framework;
using WatchInput.Editor;

namespace WatchInput.Tests.Editor
{
    [TestFixture]
    public class FixEmbedPhaseOrderTests
    {
        private string _tempDir;
        private string _pbxPath;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WatchInput_FixEmbed_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _pbxPath = Path.Combine(_tempDir, "project.pbxproj");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        public void FixEmbedPhaseOrder_MovesEmbedBeforePhony_WhenPhonyIsFirst()
        {
            string originalPbxproj = @"// !$*UTF8*$!
{
	objects = {
		13B07F861A680F5B00A75B9A /* Unity-iPhone */ = {
			isa = PBXNativeTarget;
			buildPhases = (
				96C62C0C240E819500000000 /* Unity Phony Postprocess Target */,
				96C62C0B240E819500000000 /* Embed Watch Content */,
			);
			name = ""Unity-iPhone"";
		};
	};
}
";
            File.WriteAllText(_pbxPath, originalPbxproj);

            bool result = WatchTargetConfigurator.FixEmbedPhaseOrder(_pbxPath);

            Assert.IsTrue(result, "FixEmbedPhaseOrder should return true upon successful reordering");
            string reorderedContent = File.ReadAllText(_pbxPath);
            int embedIdx = reorderedContent.IndexOf("/* Embed Watch Content */,", StringComparison.Ordinal);
            int phonyIdx = reorderedContent.IndexOf("/* Unity Phony Postprocess Target */,", StringComparison.Ordinal);

            Assert.IsTrue(embedIdx >= 0 && phonyIdx >= 0, "Both phases must exist in reordered output");
            Assert.IsTrue(embedIdx < phonyIdx, "'Embed Watch Content' phase must precede 'Unity Phony Postprocess Target'");
        }

        [Test]
        public void FixEmbedPhaseOrder_DoesNotModifyFile_WhenAlreadyInCorrectOrder()
        {
            string properlyOrderedPbxproj = @"// !$*UTF8*$!
{
	objects = {
		13B07F861A680F5B00A75B9A /* Unity-iPhone */ = {
			isa = PBXNativeTarget;
			buildPhases = (
				96C62C0B240E819500000000 /* Embed Watch Content */,
				96C62C0C240E819500000000 /* Unity Phony Postprocess Target */,
			);
			name = ""Unity-iPhone"";
		};
	};
}
";
            File.WriteAllText(_pbxPath, properlyOrderedPbxproj);
            DateTime lastWriteBefore = File.GetLastWriteTimeUtc(_pbxPath);

            bool result = WatchTargetConfigurator.FixEmbedPhaseOrder(_pbxPath);

            Assert.IsTrue(result, "FixEmbedPhaseOrder should return true when order is already correct");
            Assert.AreEqual(properlyOrderedPbxproj, File.ReadAllText(_pbxPath), "File content must remain unchanged");
        }

        [Test]
        public void FixEmbedPhaseOrder_ReturnsFalse_WhenPatternsMissing()
        {
            string invalidPbxproj = @"// !$*UTF8*$!
{
	objects = {
		13B07F861A680F5B00A75B9A /* Unity-iPhone */ = {
			isa = PBXNativeTarget;
			buildPhases = (
				13B07F871A680F5B00A75B9A /* Sources */,
			);
		};
	};
}
";
            File.WriteAllText(_pbxPath, invalidPbxproj);

            bool result = WatchTargetConfigurator.FixEmbedPhaseOrder(_pbxPath);

            Assert.IsFalse(result, "FixEmbedPhaseOrder should return false when required phase patterns are missing");
        }
    }
}
