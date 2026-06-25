using NUnit.Framework;
using UnityCliBridge.Helpers;

namespace UnityCliBridge.Tests.Helpers
{
    [TestFixture]
    public class CapturePathResolverTests
    {
        [Test]
        public void GetCaptureDirectory_ShouldUseSingularCaptureFolderAndForwardSlashes()
        {
            var directory = CapturePathResolver.GetCaptureDirectory(@"C:\Repo\UnityProject");

            Assert.AreEqual("C:/Repo/UnityProject/.unity/capture", directory);
            Assert.IsFalse(directory.Contains("\\"));
        }

        [Test]
        public void BuildCaptureFilePath_ShouldPlaceMediaUnderUnifiedCaptureDirectory()
        {
            var path = CapturePathResolver.BuildCaptureFilePath(
                "/work/UnityProject",
                "image",
                "game",
                "2026-06-23_12-00-00",
                ".png");

            Assert.AreEqual("/work/UnityProject/.unity/capture/image_game_2026-06-23_12-00-00.png", path);
        }
    }
}
