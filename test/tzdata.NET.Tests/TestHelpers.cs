using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace tzdata.Tests
{
    internal static class TestData
    {
        public static readonly string Sample = string.Join("\n",
            "# a comment line",
            "Rule US 1967 2006 - Oct lastSun 2:00 0 S # trailing comment",
            "Rule US 2007 max - Mar Sun>=8 2:00 1:00 D",
            "Rule US 2007 max - Nov Sun>=1 2:00 0 S",
            "Zone America/New_York -5:00 US E%sT",
            "Zone Etc/UTC 0 - UTC",
            "Zone Test/Eras 1:00 - CET 2000 Jan 1",
            "\t\t\t2:00 - EET",
            "Zone Test/Slash 0:00 - GMT/BST",
            "Zone Test/Offset -3:30 - %z",
            "Zone Test/FixedDst 1:00 1:00 XST",
            "Zone Test/UntilU 0 - UTC 2000 Jan 1 2:00u",
            "\t\t\t1:00 - CET",
            "Link America/New_York US/Eastern",
            "Link US/Eastern US/Eastern2",
            "");

        public static readonly string[] SampleFiles = { "northamerica" };

        public static TzDb LoadSample()
        {
            string dir = TempDir.Create();
            File.WriteAllText(Path.Combine(dir, "northamerica"), Sample);
            File.WriteAllText(Path.Combine(dir, TzDownloader.VersionFileName), "2025a");
            return TzDb.FromDirectory(dir, SampleFiles);
        }
    }

    internal static class TempDir
    {
        public static string Create()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tzdata-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void Delete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        private int _calls;

        public ConcurrentQueue<string> Urls { get; } = new ConcurrentQueue<string>();
        public int Calls { get { return _calls; } }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _calls);
            Urls.Enqueue(request.RequestUri.ToString());
            return Task.FromResult(_respond(request));
        }

        public HttpClient CreateClient()
        {
            return new HttpClient(this);
        }

        public static HttpResponseMessage Text(string text)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
        }

        /// <summary>Simulates the tz repository: NEWS lists the given version, every other file returns content.</summary>
        public static StubHandler Repo(string version, string fileContent)
        {
            return new StubHandler(r =>
                r.RequestUri.AbsolutePath.EndsWith("/NEWS", StringComparison.Ordinal)
                    ? Text("News for the tz database\n\nRelease " + version + " - 2025-01-01 00:00:00 -0800\n\n  Changes\n")
                    : Text(fileContent));
        }

        public static StubHandler Offline()
        {
            return new StubHandler(r => throw new HttpRequestException("offline"));
        }
    }
}
