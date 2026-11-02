using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace tzdata
{
    internal static class TzResolver
    {
        private struct State
        {
            public TimeSpan Save;
            public string Letter;
            public string RuleName;
            public DateTime? Start;
            public DateTime? Next;
        }

        public static string Canonical(TzData data, string id)
        {
            int guard = 0;

            while (data.Links.TryGetValue(id, out var target) && !data.Zones.ContainsKey(id) && guard++ < 10)
                id = target;

            return id;
        }

        public static TzRuleInfo Resolve(TzData data, string id, DateTime utc)
        {
            string canonical = Canonical(data, id);

            if (!data.Zones.TryGetValue(canonical, out var zone))
                throw new ArgumentException("Unknown time zone: " + id, nameof(id));

            DateTime? lineStart = null;

            foreach (var line in zone.Lines)
            {
                DateTime? until = line.UntilLocal.HasValue ? UntilUtc(data, line) : null;

                if (until.HasValue && utc >= until.Value)
                {
                    lineStart = until;
                    continue;
                }

                var st = StateAt(data, line, utc);
                DateTime? from = Max(lineStart, st.Start);
                DateTime? end = Min(until, st.Next);
                var total = line.StandardOffset + st.Save;

                return new TzRuleInfo
                {
                    ZoneId = id,
                    CanonicalId = canonical,
                    StandardOffset = line.StandardOffset,
                    SaveOffset = st.Save,
                    Abbreviation = FormatAbbreviation(line.Format, st.Save != TimeSpan.Zero, st.Letter, total),
                    Format = line.Format,
                    RuleName = st.RuleName,
                    Letter = st.Letter,
                    ValidFrom = from.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)) : null,
                    ValidUntil = end.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(end.Value, DateTimeKind.Utc)) : null
                };
            }
            throw new InvalidOperationException("No zone line applies for " + id);
        }

        private static DateTime? Max(DateTime? a, DateTime? b)
        {
            if (!a.HasValue) 
                return b;

            if (!b.HasValue) 
                return a;

            return a.Value > b.Value ? a : b;
        }

        private static DateTime? Min(DateTime? a, DateTime? b)
        {
            if (!a.HasValue) 
                return b;
            if (!b.HasValue) 
                return a;

            return a.Value < b.Value ? a : b;
        }

        private static DateTime UntilUtc(TzData data, TzZoneLine line)
        {
            var local = line.UntilLocal.Value;

            if (line.UntilType == 'u') 
                return local;

            var std = local - line.StandardOffset;

            if (line.UntilType == 's') 
                return std;

            return std - StateAt(data, line, std).Save;
        }

        private static State StateAt(TzData data, TzZoneLine line, DateTime t)
        {
            if (line.RulesName == null || !data.Rules.TryGetValue(line.RulesName, out var rules))
                return new State { Save = line.FixedSave, Letter = "" };

            int endYear = Math.Min(t.Year + 2, 9998);
            int minYear = rules.Min(r => r.FromYear);
            var trans = new List<KeyValuePair<DateTime, TzRule>>();

            foreach (var r in rules)
            {
                int to = Math.Min(r.ToYear, endYear);

                for (int y = Math.Max(r.FromYear, 1); y <= to; y++)
                    trans.Add(new KeyValuePair<DateTime, TzRule>(r.Day.Resolve(y, r.Month).Add(r.AtTime), r));
            }

            trans.Sort((a, b) => a.Key.CompareTo(b.Key));

            var initial = rules.Where(r => r.Save == TimeSpan.Zero).OrderBy(r => r.FromYear).FirstOrDefault();
            var state = new State { Save = TimeSpan.Zero, Letter = initial?.Letter ?? "", RuleName = line.RulesName };

            foreach (var kv in trans)
            {
                var r = kv.Value;
                DateTime u = r.AtType == 'u' ? kv.Key
                    : r.AtType == 's' ? kv.Key - line.StandardOffset
                    : kv.Key - line.StandardOffset - state.Save;
                if (u <= t)
                {
                    state.Save = r.Save;
                    state.Letter = r.Letter;
                    state.Start = u;
                }
                else
                {
                    state.Next = u;
                    break;
                }
            }

            return state;
        }

        private static string FormatAbbreviation(string format, bool dst, string letter, TimeSpan total)
        {
            int slash = format.IndexOf('/');

            if (slash >= 0) 
                return dst ? format.Substring(slash + 1) : format.Substring(0, slash);

            if (format.Contains("%s")) 
                return format.Replace("%s", letter ?? "");

            if (format.Contains("%z"))
            {
                var sign = total < TimeSpan.Zero ? "-" : "+";
                var a = total.Duration();
                string s = sign + a.Hours.ToString("00", CultureInfo.InvariantCulture);

                if (a.Minutes != 0 || a.Seconds != 0) 
                    s += a.Minutes.ToString("00", CultureInfo.InvariantCulture);

                if (a.Seconds != 0) 
                    s += a.Seconds.ToString("00", CultureInfo.InvariantCulture);

                return format.Replace("%z", s);
            }

            return format;
        }
    }
}
