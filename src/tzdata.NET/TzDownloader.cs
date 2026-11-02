using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace tzdata
{
    /// <summary>Version checker and downloader for the tz database (github.com/eggert/tz).</summary>
    public class TzDownloader
    {
        public static readonly string[] DefaultFiles =
        {
            "africa", "antarctica", "asia", "australasia", "backward", "backzone",
            "etcetera", "europe", "factory", "northamerica", "southamerica"
        };

        private const string RawBase = "https://raw.githubusercontent.com/eggert/tz/";

        private readonly HttpClient _http;

        public TzDownloader(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.TryParseAdd("tzdata.NET");
        }

        private async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using (var r = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                r.EnsureSuccessStatusCode();

                return await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Gets the latest released version (e.g. "2025b") from the NEWS file.</summary>
        public async Task<string> GetLatestVersionAsync(CancellationToken ct = default)
        {
            var news = await GetStringAsync(RawBase + "main/NEWS", ct).ConfigureAwait(false);
            var m = Regex.Match(news, @"^Release\s+(\S+)", RegexOptions.Multiline);

            if (!m.Success) 
                throw new InvalidDataException("Unable to determine tz version from NEWS.");

            return m.Groups[1].Value;
        }

        /// <summary>Downloads the given files for a version into a directory and writes a version file.</summary>
        public async Task DownloadAsync(string directory, string version, IEnumerable<string> files = null, CancellationToken ct = default)
        {
            var contents = new Dictionary<string, string>();

            foreach (var f in files ?? DefaultFiles)
                contents[f] = await GetStringAsync(RawBase + version + "/" + f, ct).ConfigureAwait(false);

            Directory.CreateDirectory(directory);

            foreach (var kv in contents)
                File.WriteAllText(Path.Combine(directory, kv.Key), kv.Value);

            File.WriteAllText(Path.Combine(directory, VersionFileName), version);
        }

        public const string VersionFileName = "version.txt";

        public static string ReadLocalVersion(string directory)
        {
            var p = Path.Combine(directory, VersionFileName);

            return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
        }
    }
}
