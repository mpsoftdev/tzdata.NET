using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tzdata.Tests
{
    [TestClass]
    public class TzDbTests
    {
        private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0)
        {
            return new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.Zero);
        }

        private static TzDbOptions Options(string dir, StubHandler handler, TimeSpan? interval = null)
        {
            return new TzDbOptions
            {
                CacheDirectory = dir,
                Files = TestData.SampleFiles,
                HttpClient = handler.CreateClient(),
                CheckInterval = interval ?? TimeSpan.FromDays(1)
            };
        }

        // ---- FromDirectory / data access ----

        [TestMethod]
        public void FromDirectory_LoadsDataAndVersion()
        {
            var db = TestData.LoadSample();

            Assert.AreEqual("2025a", db.Version);
            Assert.AreEqual(7, db.Data.Zones.Count);
            Assert.AreEqual(7, db.CanonicalIds.Count());
        }

        [TestMethod]
        public void FromDirectory_MissingVersionFile_VersionIsNull()
        {
            string dir = TempDir.Create();
            File.WriteAllText(Path.Combine(dir, "northamerica"), TestData.Sample);

            Assert.IsNull(TzDb.FromDirectory(dir, TestData.SampleFiles).Version);
        }

        [TestMethod]
        public void FromDirectory_MissingFile_Throws()
        {
            string dir = TempDir.Create();

            Assert.ThrowsExactly<FileNotFoundException>(() => TzDb.FromDirectory(dir, TestData.SampleFiles));
        }

        [TestMethod]
        public void FromDirectory_BackzoneIgnoredByDefault()
        {
            var db = LoadWithBackzone(includeBackzone: false);

            Assert.IsFalse(db.Data.Zones.ContainsKey("Test/Alias"));
            Assert.AreEqual("America/New_York", db.GetCanonicalId("Test/Alias"));
        }

        [TestMethod]
        public void FromDirectory_BackzoneReplacesLinks_WhenIncluded()
        {
            var db = LoadWithBackzone(includeBackzone: true);

            Assert.IsTrue(db.Data.Zones.ContainsKey("Test/Alias"));
            Assert.AreEqual("Test/Alias", db.GetCanonicalId("Test/Alias"));
            Assert.AreEqual(TimeSpan.FromHours(-4), db.GetRule("Test/Alias", Utc(2024, 1, 1)).StandardOffset);
            CollectionAssert.DoesNotContain(db.GetAliases("America/New_York").ToList(), "Test/Alias");
        }

        private static TzDb LoadWithBackzone(bool includeBackzone)
        {
            string dir = TempDir.Create();
            File.WriteAllText(Path.Combine(dir, "northamerica"), TestData.Sample + "\nLink America/New_York Test/Alias\n");
            File.WriteAllText(Path.Combine(dir, "backzone"), "Zone Test/Alias -4:00 - XXT\n");
            return TzDb.FromDirectory(dir, new[] { "northamerica", "backzone" }, includeBackzone);
        }

        // ---- IsKnown / canonical / aliases ----

        [TestMethod]
        public void IsKnown_ZonesAndLinks()
        {
            var db = TestData.LoadSample();

            Assert.IsTrue(db.IsKnown("America/New_York"));
            Assert.IsTrue(db.IsKnown("US/Eastern"));
            Assert.IsTrue(db.IsKnown("us/eastern"));
            Assert.IsFalse(db.IsKnown("Nowhere/Land"));
        }

        [TestMethod]
        public void GetCanonicalId_ResolvesAliasAndChains()
        {
            var db = TestData.LoadSample();

            Assert.AreEqual("America/New_York", db.GetCanonicalId("US/Eastern"));
            Assert.AreEqual("America/New_York", db.GetCanonicalId("US/Eastern2"));
            Assert.AreEqual("America/New_York", db.GetCanonicalId("us/eastern"));
        }

        [TestMethod]
        public void GetCanonicalId_CanonicalReturnsItself()
        {
            Assert.AreEqual("Etc/UTC", TestData.LoadSample().GetCanonicalId("Etc/UTC"));
        }

        [TestMethod]
        public void GetCanonicalId_Unknown_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => TestData.LoadSample().GetCanonicalId("Nowhere/Land"));
        }

        [TestMethod]
        public void GetAliases_ReturnsSortedAliasesIncludingChained()
        {
            var aliases = TestData.LoadSample().GetAliases("America/New_York");

            CollectionAssert.AreEqual(new[] { "US/Eastern", "US/Eastern2" }, aliases.ToList());
        }

        [TestMethod]
        public void GetAliases_AcceptsAliasAsInput()
        {
            var aliases = TestData.LoadSample().GetAliases("US/Eastern");

            CollectionAssert.AreEqual(new[] { "US/Eastern", "US/Eastern2" }, aliases.ToList());
        }

        [TestMethod]
        public void GetAliases_NoAliases_ReturnsEmpty()
        {
            Assert.AreEqual(0, TestData.LoadSample().GetAliases("Etc/UTC").Count);
        }

        [TestMethod]
        public void GetAliases_Unknown_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => TestData.LoadSample().GetAliases("Nowhere/Land"));
        }

        // ---- GetRule ----

        [TestMethod]
        public void GetRule_Summer_IsDaylightSaving()
        {
            var info = TestData.LoadSample().GetRule("America/New_York", Utc(2024, 7, 1, 12));

            Assert.AreEqual("America/New_York", info.ZoneId);
            Assert.AreEqual("America/New_York", info.CanonicalId);
            Assert.AreEqual(TimeSpan.FromHours(-5), info.StandardOffset);
            Assert.AreEqual(TimeSpan.FromHours(1), info.SaveOffset);
            Assert.AreEqual(TimeSpan.FromHours(-4), info.TotalOffset);
            Assert.IsTrue(info.IsDaylightSaving);
            Assert.AreEqual("EDT", info.Abbreviation);
            Assert.AreEqual("E%sT", info.Format);
            Assert.AreEqual("US", info.RuleName);
            Assert.AreEqual("D", info.Letter);
            Assert.AreEqual(Utc(2024, 3, 10, 7), info.ValidFrom);
            Assert.AreEqual(Utc(2024, 11, 3, 6), info.ValidUntil);
        }

        [TestMethod]
        public void GetRule_Winter_IsStandardTime()
        {
            var info = TestData.LoadSample().GetRule("America/New_York", Utc(2024, 1, 15, 12));

            Assert.AreEqual(TimeSpan.Zero, info.SaveOffset);
            Assert.AreEqual(TimeSpan.FromHours(-5), info.TotalOffset);
            Assert.IsFalse(info.IsDaylightSaving);
            Assert.AreEqual("EST", info.Abbreviation);
            Assert.AreEqual(Utc(2023, 11, 5, 6), info.ValidFrom);
            Assert.AreEqual(Utc(2024, 3, 10, 7), info.ValidUntil);
        }

        [TestMethod]
        public void GetRule_TransitionBoundaries()
        {
            var db = TestData.LoadSample();

            Assert.AreEqual("EST", db.GetRule("America/New_York", Utc(2024, 3, 10, 6, 59)).Abbreviation);
            Assert.AreEqual("EDT", db.GetRule("America/New_York", Utc(2024, 3, 10, 7)).Abbreviation);
            Assert.AreEqual("EDT", db.GetRule("America/New_York", Utc(2024, 11, 3, 5, 59)).Abbreviation);
            Assert.AreEqual("EST", db.GetRule("America/New_York", Utc(2024, 11, 3, 6)).Abbreviation);
        }

        [TestMethod]
        public void GetRule_Alias_ResolvesToCanonical()
        {
            var info = TestData.LoadSample().GetRule("US/Eastern", Utc(2024, 7, 1));

            Assert.AreEqual("US/Eastern", info.ZoneId);
            Assert.AreEqual("America/New_York", info.CanonicalId);
            Assert.AreEqual("EDT", info.Abbreviation);
        }

        [TestMethod]
        public void GetRule_AcceptsOffsetInstants()
        {
            var instant = new DateTimeOffset(2024, 7, 1, 8, 0, 0, TimeSpan.FromHours(-4));

            Assert.AreEqual("EDT", TestData.LoadSample().GetRule("America/New_York", instant).Abbreviation);
        }

        [TestMethod]
        public void GetRule_FixedZone_IsUnbounded()
        {
            var info = TestData.LoadSample().GetRule("Etc/UTC", Utc(2024, 1, 1));

            Assert.AreEqual("UTC", info.Abbreviation);
            Assert.AreEqual(TimeSpan.Zero, info.TotalOffset);
            Assert.IsNull(info.RuleName);
            Assert.IsNull(info.ValidFrom);
            Assert.IsNull(info.ValidUntil);
        }

        [TestMethod]
        public void GetRule_MultipleEras_PickCorrectEra()
        {
            var db = TestData.LoadSample();

            var before = db.GetRule("Test/Eras", Utc(1999, 6, 1));
            Assert.AreEqual("CET", before.Abbreviation);
            Assert.AreEqual(TimeSpan.FromHours(1), before.StandardOffset);
            Assert.IsNull(before.ValidFrom);
            Assert.AreEqual(Utc(1999, 12, 31, 23), before.ValidUntil);

            var after = db.GetRule("Test/Eras", Utc(2001, 6, 1));
            Assert.AreEqual("EET", after.Abbreviation);
            Assert.AreEqual(TimeSpan.FromHours(2), after.StandardOffset);
            Assert.AreEqual(Utc(1999, 12, 31, 23), after.ValidFrom);
            Assert.IsNull(after.ValidUntil);
        }

        [TestMethod]
        public void GetRule_UntilInUtc()
        {
            var info = TestData.LoadSample().GetRule("Test/UntilU", Utc(1999, 1, 1));

            Assert.AreEqual(Utc(2000, 1, 1, 2), info.ValidUntil);
        }

        [TestMethod]
        public void GetRule_SlashFormat_UsesStandardPart()
        {
            Assert.AreEqual("GMT", TestData.LoadSample().GetRule("Test/Slash", Utc(2024, 1, 1)).Abbreviation);
        }

        [TestMethod]
        public void GetRule_NumericFormat_UsesOffset()
        {
            Assert.AreEqual("-0330", TestData.LoadSample().GetRule("Test/Offset", Utc(2024, 1, 1)).Abbreviation);
        }

        [TestMethod]
        public void GetRule_FixedSave_IsDaylightSaving()
        {
            var info = TestData.LoadSample().GetRule("Test/FixedDst", Utc(2024, 1, 1));

            Assert.IsTrue(info.IsDaylightSaving);
            Assert.AreEqual(TimeSpan.FromHours(2), info.TotalOffset);
            Assert.AreEqual("XST", info.Abbreviation);
        }

        [TestMethod]
        public void GetRule_Unknown_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => TestData.LoadSample().GetRule("Nowhere/Land", Utc(2024, 1, 1)));
        }

        // ---- LoadAsync / versioning ----

        [TestMethod]
        public async Task LoadAsync_EmptyCache_Downloads()
        {
            string dir = TempDir.Create();
            try
            {
                var handler = StubHandler.Repo("2025b", TestData.Sample);

                var db = await TzDb.LoadAsync(Options(dir, handler));

                Assert.AreEqual("2025b", db.Version);
                Assert.AreEqual("2025b", TzDownloader.ReadLocalVersion(dir));
                Assert.IsTrue(File.Exists(Path.Combine(dir, "northamerica")));
                Assert.IsTrue(db.IsKnown("America/New_York"));
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task LoadAsync_FreshCache_DoesNotUseNetwork()
        {
            string dir = TempDir.Create();
            try
            {
                await TzDb.LoadAsync(Options(dir, StubHandler.Repo("2025b", TestData.Sample)));
                var handler = StubHandler.Repo("2025c", TestData.Sample);

                var db = await TzDb.LoadAsync(Options(dir, handler, TimeSpan.FromDays(30)));

                Assert.AreEqual(0, handler.Calls);
                Assert.AreEqual("2025b", db.Version);
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task LoadAsync_ForceDownload_DownloadsAgain()
        {
            string dir = TempDir.Create();
            try
            {
                await TzDb.LoadAsync(Options(dir, StubHandler.Repo("2025b", TestData.Sample)));
                var handler = StubHandler.Repo("2025b", TestData.Sample);

                await TzDb.LoadAsync(Options(dir, handler, TimeSpan.FromDays(30)), forceDownload: true);

                Assert.IsTrue(handler.Urls.Any(u => u.EndsWith("/2025b/northamerica", StringComparison.Ordinal)));
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task LoadAsync_StaleCacheWithNewerVersion_Updates()
        {
            string dir = TempDir.Create();
            try
            {
                await TzDb.LoadAsync(Options(dir, StubHandler.Repo("2025b", TestData.Sample)));
                File.SetLastWriteTimeUtc(Path.Combine(dir, TzDownloader.VersionFileName), DateTime.UtcNow.AddDays(-10));

                var db = await TzDb.LoadAsync(Options(dir, StubHandler.Repo("2025c", TestData.Sample)));

                Assert.AreEqual("2025c", db.Version);
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task LoadAsync_StaleCacheSameVersion_OnlyChecksVersion()
        {
            string dir = TempDir.Create();
            try
            {
                await TzDb.LoadAsync(Options(dir, StubHandler.Repo("2025b", TestData.Sample)));
                string versionFile = Path.Combine(dir, TzDownloader.VersionFileName);
                File.SetLastWriteTimeUtc(versionFile, DateTime.UtcNow.AddDays(-10));
                var handler = StubHandler.Repo("2025b", TestData.Sample);

                var db = await TzDb.LoadAsync(Options(dir, handler));

                Assert.AreEqual(1, handler.Calls);
                Assert.AreEqual("2025b", db.Version);
                Assert.IsTrue(File.GetLastWriteTimeUtc(versionFile) > DateTime.UtcNow.AddDays(-1));
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task LoadAsync_StaleCacheOffline_UsesCachedData()
        {
            string dir = TempDir.Create();
            try
            {
                await TzDb.LoadAsync(Options(dir, StubHandler.Repo("2025b", TestData.Sample)));
                File.SetLastWriteTimeUtc(Path.Combine(dir, TzDownloader.VersionFileName), DateTime.UtcNow.AddDays(-10));

                var db = await TzDb.LoadAsync(Options(dir, StubHandler.Offline()));

                Assert.AreEqual("2025b", db.Version);
                Assert.IsTrue(db.IsKnown("America/New_York"));
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task LoadAsync_EmptyCacheOffline_Throws()
        {
            string dir = TempDir.Create();
            try
            {
                await Assert.ThrowsExactlyAsync<System.Net.Http.HttpRequestException>(
                    () => TzDb.LoadAsync(Options(dir, StubHandler.Offline())));
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task LoadAsync_IncompleteCache_Downloads()
        {
            string dir = TempDir.Create();
            try
            {
                File.WriteAllText(Path.Combine(dir, TzDownloader.VersionFileName), "2024a");
                var handler = StubHandler.Repo("2025b", TestData.Sample);

                var db = await TzDb.LoadAsync(Options(dir, handler));

                Assert.AreEqual("2025b", db.Version);
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task LoadAsync_Cancelled_Throws()
        {
            string dir = TempDir.Create();
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource())
                {
                    cts.Cancel();
                    var handler = new StubHandler(r => StubHandler.Text(""));
                    var options = Options(dir, handler);

                    await Assert.ThrowsAsync<OperationCanceledException>(() => TzDb.LoadAsync(options, false, cts.Token));
                }
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public async Task GetLatestVersionAsync_And_IsUpToDateAsync()
        {
            string dir = TempDir.Create();
            try
            {
                var db = await TzDb.LoadAsync(Options(dir, StubHandler.Repo("2025b", TestData.Sample)));

                // Same client is reused by the loaded instance; it always answers with 2025b.
                Assert.AreEqual("2025b", await db.GetLatestVersionAsync());
                Assert.IsTrue(await db.IsUpToDateAsync());
            }
            finally
            {
                TempDir.Delete(dir);
            }
        }

        [TestMethod]
        public void TzDbOptions_Defaults()
        {
            var o = new TzDbOptions();

            Assert.IsNull(o.CacheDirectory);
            Assert.IsNull(o.Files);
            Assert.IsFalse(o.IncludeBackzone);
            Assert.IsNull(o.HttpClient);
            Assert.AreEqual(TimeSpan.FromDays(1), o.CheckInterval);
        }
    }
}
