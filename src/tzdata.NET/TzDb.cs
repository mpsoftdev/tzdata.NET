using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace tzdata
{
    public class TzDbOptions
    {
        /// <summary>Where the downloaded files are stored. Defaults to LocalApplicationData/tzdata.NET.</summary>
        public string CacheDirectory { get; set; }
        /// <summary>Source files to download and parse. Defaults to <see cref="TzDownloader.DefaultFiles"/>.</summary>
        public IList<string> Files { get; set; }
        /// <summary>When true, zones from backzone replace links of the same name.</summary>
        public bool IncludeBackzone { get; set; }
        /// <summary>How long a cached copy is trusted before checking for a newer version.</summary>
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromDays(1);
        public HttpClient HttpClient { get; set; }
    }

    public class TzDb
    {
        private readonly TzData _data;
        private readonly TzDownloader _downloader;

        private TzDb(TzData data, string version, TzDownloader downloader)
        {
            _data = data;
            Version = version;
            _downloader = downloader;
        }

        /// <summary>The tz release version of the loaded data (e.g. "2025b").</summary>
        public string Version { get; }

        /// <summary>Loads the database, downloading it if missing, outdated, or when forced.</summary>
        public static async Task<TzDb> LoadAsync(TzDbOptions options = null, bool forceDownload = false, CancellationToken ct = default(CancellationToken))
        {
            options = options ?? new TzDbOptions();
            string dir = options.CacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tzdata.NET");
            var files = options.Files ?? TzDownloader.DefaultFiles;
            var downloader = new TzDownloader(options.HttpClient);

            string local = TzDownloader.ReadLocalVersion(dir);
            bool complete = local != null && files.All(f => File.Exists(Path.Combine(dir, f)));
            string versionFile = Path.Combine(dir, TzDownloader.VersionFileName);

            if (!complete || forceDownload)
            {
                string latest = await downloader.GetLatestVersionAsync(ct).ConfigureAwait(false);
                await downloader.DownloadAsync(dir, latest, files, ct).ConfigureAwait(false);
            }
            else if (DateTime.UtcNow - File.GetLastWriteTimeUtc(versionFile) > options.CheckInterval)
            {
                try
                {
                    string latest = await downloader.GetLatestVersionAsync(ct).ConfigureAwait(false);
                    if (!string.Equals(latest, local, StringComparison.Ordinal))
                        await downloader.DownloadAsync(dir, latest, files, ct).ConfigureAwait(false);
                    else
                        File.SetLastWriteTimeUtc(versionFile, DateTime.UtcNow);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // offline: keep using the cached copy
                }
            }

            var db = FromDirectory(dir, files, options.IncludeBackzone);
            return new TzDb(db._data, db.Version, downloader);
        }

        /// <summary>Loads from an already populated directory without any network access.</summary>
        public static TzDb FromDirectory(string directory, IEnumerable<string> files = null, bool includeBackzone = false)
        {
            var data = new TzData();
            var list = (files ?? TzDownloader.DefaultFiles).ToList();
            foreach (var f in list.Where(f => f != "backzone"))
                TzParser.ParseInto(data, File.ReadAllText(Path.Combine(directory, f)));

            string bz = Path.Combine(directory, "backzone");
            if (includeBackzone && list.Contains("backzone") && File.Exists(bz))
            {
                var back = new TzData();
                TzParser.ParseInto(back, File.ReadAllText(bz));
                foreach (var z in back.Zones)
                {
                    data.Zones[z.Key] = z.Value;
                    data.Links.Remove(z.Key);
                }
                foreach (var r in back.Rules)
                    if (!data.Rules.ContainsKey(r.Key)) data.Rules[r.Key] = r.Value;
            }
            return new TzDb(data, TzDownloader.ReadLocalVersion(directory), null);
        }

        /// <summary>Checks whether a newer release exists upstream.</summary>
        public async Task<bool> IsUpToDateAsync(CancellationToken ct = default)
        {
            return string.Equals(await GetLatestVersionAsync(ct).ConfigureAwait(false), Version, StringComparison.Ordinal);
        }

        public Task<string> GetLatestVersionAsync(CancellationToken ct = default)
        {
            return (_downloader ?? new TzDownloader()).GetLatestVersionAsync(ct);
        }

        /// <summary>Raw parsed data.</summary>
        public TzData Data { get { return _data; } }

        public IEnumerable<string> CanonicalIds { get { return _data.Zones.Keys; } }

        public bool IsKnown(string id)
        {
            return _data.Zones.ContainsKey(id) || _data.Links.ContainsKey(id);
        }

        /// <summary>Returns the canonical zone for an alias (or the id itself if already canonical).</summary>
        public string GetCanonicalId(string id)
        {
            if (!IsKnown(id)) throw new ArgumentException("Unknown time zone: " + id, nameof(id));
            return TzResolver.Canonical(_data, id);
        }

        /// <summary>Returns all aliases that resolve to the given canonical zone.</summary>
        public IReadOnlyList<string> GetAliases(string canonicalId)
        {
            string canon = GetCanonicalId(canonicalId);
            return _data.Links.Keys
                .Where(k => !_data.Zones.ContainsKey(k) && string.Equals(TzResolver.Canonical(_data, k), canon, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Gets the rule (offset, abbreviation, boundaries) in effect for a zone at an instant.</summary>
        public TzRuleInfo GetRule(string id, DateTimeOffset instant)
        {
            return TzResolver.Resolve(_data, id, instant.UtcDateTime);
        }
    }
}
