using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tzdata.Tests
{
    [TestClass]
    public class TzDownloaderTests
    {
        [TestMethod]
        public void DefaultFiles_ContainsExpectedFiles()
        {
            foreach (var name in new[] { "africa", "antarctica", "asia", "australasia", "backzone", "etcetera", "europe", "northamerica", "southamerica" })
                CollectionAssert.Contains(TzDownloader.DefaultFiles, name);
        }

        [TestMethod]
        public async Task GetLatestVersionAsync_ParsesFirstReleaseFromNews()
        {
            var handler = StubHandler.Repo("2025b", "");
            var downloader = new TzDownloader(handler.CreateClient());

            Assert.AreEqual("2025b", await downloader.GetLatestVersionAsync());
            Assert.IsTrue(handler.Urls.Single().EndsWith("/NEWS", StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task GetLatestVersionAsync_NoRelease_Throws()
        {
            var handler = new StubHandler(r => StubHandler.Text("nothing useful here"));
            var downloader = new TzDownloader(handler.CreateClient());

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => downloader.GetLatestVersionAsync());
        }

        [TestMethod]
        public async Task GetLatestVersionAsync_HttpError_Throws()
        {
            var handler = new StubHandler(r => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            var downloader = new TzDownloader(handler.CreateClient());

            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => downloader.GetLatestVersionAsync());
        }

        [TestMethod]
        public async Task DownloadAsync_WritesFilesAndVersion()
        {
            string dir = Path.Combine(TempDir.Create(), "nested");
            try
            {
                var handler = StubHandler.Repo("2025b", "Zone A/One 0 - UTC");
                var downloader = new TzDownloader(handler.CreateClient());

                await downloader.DownloadAsync(dir, "2025b", new[] { "africa", "asia" });

                Assert.AreEqual("Zone A/One 0 - UTC", File.ReadAllText(Path.Combine(dir, "africa")));
                Assert.IsTrue(File.Exists(Path.Combine(dir, "asia")));
                Assert.AreEqual("2025b", TzDownloader.ReadLocalVersion(dir));
                Assert.IsTrue(handler.Urls.Any(u => u.EndsWith("/2025b/africa", StringComparison.Ordinal)));
                Assert.IsTrue(handler.Urls.Any(u => u.EndsWith("/2025b/asia", StringComparison.Ordinal)));
            }
            finally
            {
                TempDir.Delete(Path.GetDirectoryName(dir));
            }
        }

        [TestMethod]
        public async Task DownloadAsync_DefaultFiles_DownloadsAll()
        {
            string dir = TempDir.Create();
            try
            {
                var handler = StubHandler.Repo("2025b", "x");
                await new TzDownloader(handler.CreateClient()).DownloadAsync(dir, "2025b");

                Assert.AreEqual(TzDownloader.DefaultFiles.Length, handler.Calls);
                foreach (var f in TzDownloader.DefaultFiles)
                    Assert.IsTrue(File.Exists(Path.Combine(dir, f)), f);
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task DownloadAsync_Failure_LeavesNoVersionFile()
        {
            string dir = TempDir.Create();
            try
            {
                var handler = new StubHandler(r => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
                var downloader = new TzDownloader(handler.CreateClient());

                await Assert.ThrowsExactlyAsync<HttpRequestException>(() => downloader.DownloadAsync(dir, "2025b", new[] { "africa" }));

                Assert.IsNull(TzDownloader.ReadLocalVersion(dir));
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public void ReadLocalVersion_MissingFile_ReturnsNull()
        {
            Assert.IsNull(TzDownloader.ReadLocalVersion(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        }

        [TestMethod]
        public void ReadLocalVersion_TrimsWhitespace()
        {
            string dir = TempDir.Create();
            try
            {
                File.WriteAllText(Path.Combine(dir, TzDownloader.VersionFileName), "2024a\r\n");

                Assert.AreEqual("2024a", TzDownloader.ReadLocalVersion(dir));
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }
    }
}
