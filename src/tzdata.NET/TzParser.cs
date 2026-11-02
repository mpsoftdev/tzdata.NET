using System;
using System.Collections.Generic;
using System.Globalization;

namespace tzdata
{
    /// <summary>Parser for the IANA tz source files (africa, asia, backzone, ...).</summary>
    public static class TzParser
    {
        public static TzData Parse(IEnumerable<string> fileContents)
        {
            var data = new TzData();

            foreach (var c in fileContents) 
                ParseInto(data, c);

            return data;
        }

        public static void ParseInto(TzData data, string content)
        {
            TzZone current = null;

            foreach (var raw in content.Split('\n'))
            {
                var line = raw;
                int hash = line.IndexOf('#');

                if (hash >= 0) 
                    line = line.Substring(0, hash);

                var t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

                if (t.Length == 0) 
                    continue;

                switch (t[0])
                {
                    case "Rule":
                        current = null;
                        AddRule(data, t);
                        break;
                    case "Zone":
                        current = new TzZone { Name = t[1] };
                        data.Zones[current.Name] = current;
                        current.Lines.Add(ParseZoneLine(t, 2));
                        break;
                    case "Link":
                        current = null;
                        data.Links[t[2]] = t[1];
                        break;
                    default:
                        current?.Lines.Add(ParseZoneLine(t, 0));
                        break;
                }
            }
        }

        private static void AddRule(TzData data, string[] t)
        {
            int from = int.Parse(t[2], CultureInfo.InvariantCulture);
            int to = t[3] == "only" ? from : t[3] == "max" ? int.MaxValue
                : ParseYear(t[3]);

            var at = ParseTime(t[7], out char atType);

            var rule = new TzRule
            {
                Name = t[1],
                FromYear = from,
                ToYear = to,
                Month = ParseMonth(t[5]),
                Day = ParseDay(t[6]),
                AtTime = at,
                AtType = atType,
                Save = ParseTime(t[8], out _),
                Letter = t[9] == "-" ? "" : t[9]
            };

            if (!data.Rules.TryGetValue(rule.Name, out var list))
                data.Rules[rule.Name] = list = new List<TzRule>();

            list.Add(rule);
        }

        private static int ParseYear(string s)
        {
            if (s == "min") 
                return int.MinValue;

            if (s == "max") 
                return int.MaxValue;

            return int.Parse(s, CultureInfo.InvariantCulture);
        }

        private static TzZoneLine ParseZoneLine(string[] t, int i)
        {
            var zl = new TzZoneLine
            {
                StandardOffset = ParseTime(t[i], out _),
                Format = t[i + 2],
                UntilType = 'w'
            };

            string rules = t[i + 1];

            if (rules != "-")
            {
                if (char.IsDigit(rules[0]) || rules[0] == '-') zl.FixedSave = ParseTime(rules, out _);
                else zl.RulesName = rules;
            }

            int u = i + 3;

            if (t.Length > u)
            {
                int year = int.Parse(t[u], CultureInfo.InvariantCulture);
                int month = t.Length > u + 1 ? ParseMonth(t[u + 1]) : 1;
                var day = t.Length > u + 2 ? ParseDay(t[u + 2]) : new DaySpec { Day = 1 };
                var time = TimeSpan.Zero;
                char type = 'w';
                if (t.Length > u + 3) time = ParseTime(t[u + 3], out type);
                zl.UntilLocal = day.Resolve(year, month).Add(time);
                zl.UntilType = type;
            }

            return zl;
        }

        private static readonly string[] Months =
            { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        private static int ParseMonth(string s)
        {
            var p = s.Substring(0, 3).ToLowerInvariant();
            int i = Array.IndexOf(Months, p);

            if (i < 0) 
                throw new FormatException("Invalid month: " + s);

            return i + 1;
        }

        private static DayOfWeek ParseDow(string s)
        {
            switch (s.Substring(0, 3).ToLowerInvariant())
            {
                case "sun": return DayOfWeek.Sunday;
                case "mon": return DayOfWeek.Monday;
                case "tue": return DayOfWeek.Tuesday;
                case "wed": return DayOfWeek.Wednesday;
                case "thu": return DayOfWeek.Thursday;
                case "fri": return DayOfWeek.Friday;
                case "sat": return DayOfWeek.Saturday;
            }

            throw new FormatException("Invalid weekday: " + s);
        }

        private static DaySpec ParseDay(string s)
        {
            if (s.StartsWith("last", StringComparison.Ordinal))
                return new DaySpec { Mode = 1, Dow = ParseDow(s.Substring(4)) };

            int i = s.IndexOf(">=", StringComparison.Ordinal);

            if (i > 0)
                return new DaySpec { Mode = 2, Dow = ParseDow(s.Substring(0, i)), Day = int.Parse(s.Substring(i + 2), CultureInfo.InvariantCulture) };

            i = s.IndexOf("<=", StringComparison.Ordinal);

            if (i > 0)
                return new DaySpec { Mode = 3, Dow = ParseDow(s.Substring(0, i)), Day = int.Parse(s.Substring(i + 2), CultureInfo.InvariantCulture) };

            return new DaySpec { Day = int.Parse(s, CultureInfo.InvariantCulture) };
        }

        /// <summary>Parses [-]h[:mm[:ss]] with optional w/s/u/g/z/d suffix.</summary>
        private static TimeSpan ParseTime(string s, out char type)
        {
            type = 'w';

            if (s == "-") 
                return TimeSpan.Zero;

            char last = s[s.Length - 1];

            if (char.IsLetter(last))
            {
                s = s.Substring(0, s.Length - 1);

                if (last == 's') 
                    type = 's';
                else if (last == 'u' || last == 'g' || last == 'z') 
                    type = 'u';
            }

            bool neg = s.StartsWith("-", StringComparison.Ordinal);

            if (neg) 
                s = s.Substring(1);

            var p = s.Split(':');
            double h = double.Parse(p[0], CultureInfo.InvariantCulture);
            double m = p.Length > 1 ? double.Parse(p[1], CultureInfo.InvariantCulture) : 0;
            double sec = p.Length > 2 ? double.Parse(p[2], CultureInfo.InvariantCulture) : 0;
            var ts = TimeSpan.FromSeconds(h * 3600 + m * 60 + sec);

            return neg ? -ts : ts;
        }
    }
}
