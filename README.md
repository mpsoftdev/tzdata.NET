# tzdata.NET

A lightweight .NET library that downloads, parses and queries the IANA Time Zone Database (tzdata) source files from [eggert/tz](https://github.com/eggert/tz).

It is meant as a **companion** to libraries such as TimeZoneConverter, not a replacement. Its main feature is that the data can be updated at runtime, without waiting for a new library release.

Targets: .NET Standard 2.0, .NET Framework 4.6.2, .NET 8, .NET 9, .NET 10.

## Features

- **Downloader**: fetches the tz source files when missing, outdated, or on explicit request.
- **Version checker**: compares the local release with the latest upstream release.
- **Parser**: reads `Rule`, `Zone` and `Link` lines from `africa`, `antarctica`, `asia`, `australasia`, `backward`, `backzone`, `etcetera`, `europe`, `factory`, `northamerica`, `southamerica` (or any other file list you provide).
- **Canonical resolver**: finds the canonical zone of an alias.
- **Alias resolver**: lists the aliases of a canonical zone.
- **Rule provider**: returns the rule in effect for a zone at a given instant (offsets, abbreviation, validity boundaries, rule name and letter).

## Usage

```csharp
using tzdata;

// Loads from the cache, downloading when missing or older than the latest release.
TzDb db = await TzDb.LoadAsync();

Console.WriteLine(db.Version);                        // e.g. "2025b"

// Aliases and canonical names
string canonical = db.GetCanonicalId("US/Eastern");   // "America/New_York"
var aliases = db.GetAliases("America/New_York");      // e.g. "US/Eastern", ...

// Rule in effect at an instant
TzRuleInfo rule = db.GetRule("America/New_York", DateTimeOffset.UtcNow);
Console.WriteLine(rule.Abbreviation);                 // "EST" / "EDT"
Console.WriteLine(rule.TotalOffset);                  // -05:00:00 / -04:00:00
Console.WriteLine(rule.IsDaylightSaving);
Console.WriteLine(rule.ValidFrom);                    // start of this period (null if unbounded)
Console.WriteLine(rule.ValidUntil);                   // next transition / "until" boundary (null if unbounded)
```

### Options

```csharp
var db = await TzDb.LoadAsync(new TzDbOptions
{
    CacheDirectory  = @"C:\data\tz",              // default: LocalApplicationData/tzdata.NET
    Files           = new[] { "africa", "asia" }, // default: TzDownloader.DefaultFiles
    IncludeBackzone = true,                       // backzone zones replace links of the same name
    CheckInterval   = TimeSpan.FromDays(7),       // default: 1 day
    HttpClient      = myHttpClient                // optional
});

// Force a fresh download
var fresh = await TzDb.LoadAsync(forceDownload: true);
```

Loading behavior:

1. Cache missing or incomplete: downloads the latest release.
2. Cache older than `CheckInterval`: checks the latest version and downloads only if it differs. If offline, the cached data is used.
3. Otherwise: uses the cache with no network access.

### Version checking

```csharp
string latest = await db.GetLatestVersionAsync();
bool upToDate = await db.IsUpToDateAsync();
```

### Offline use

```csharp
var db = TzDb.FromDirectory(@"C:\data\tz");
```

### Lower-level components

- `TzDownloader`: `GetLatestVersionAsync`, `DownloadAsync`, `ReadLocalVersion`.
- `TzParser`: `Parse`, `ParseInto`.
- `TzDb.Data`: the parsed `TzData` (`Rules`, `Zones`, `Links`).

## Notes and limitations

- Rule resolution evaluates transitions from the tz source rules; it is not a full re-implementation of `zic` and has not been validated against every zone in the database.
- `TzRuleInfo.ValidFrom`/`ValidUntil` reflect the nearest transition or zone-line boundary.
- Unknown zone IDs throw `ArgumentException`.

## Testing

```
dotnet test
```

Tests use fake HTTP handlers and sample data; no network access is required.

## License

MIT. See [LICENSE.txt](LICENSE.txt).
