using System;
using System.Collections.Generic;

namespace tzdata
{
    /// <summary>Day specification used by Rule ON fields and Zone UNTIL fields.</summary>
    internal sealed class DaySpec
    {
        public int Day;
        public DayOfWeek Dow;
        /// <summary>0 = exact day, 1 = last weekday, 2 = weekday on or after day, 3 = weekday on or before day.</summary>
        public int Mode;

        public DateTime Resolve(int year, int month)
        {
            if (Mode == 0) 
                return new DateTime(year, month, Day);

            if (Mode == 1)
            {
                var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));

                while (d.DayOfWeek != Dow) 
                    d = d.AddDays(-1);

                return d;
            }

            var date = new DateTime(year, month, Day);

            if (Mode == 2)
            {
                while (date.DayOfWeek != Dow) 
                    date = date.AddDays(1);
            }
            else
            {
                while (date.DayOfWeek != Dow) 
                    date = date.AddDays(-1);
            }

            return date;
        }
    }

    /// <summary>A "Rule" line of the tz source files.</summary>
    public sealed class TzRule
    {
        public string Name { get; internal set; }
        public int FromYear { get; internal set; }
        public int ToYear { get; internal set; }
        public int Month { get; internal set; }
        internal DaySpec Day;
        public TimeSpan AtTime { get; internal set; }
        /// <summary>'w' wall clock, 's' standard time, 'u' UTC.</summary>
        public char AtType { get; internal set; }
        public TimeSpan Save { get; internal set; }
        public string Letter { get; internal set; }
    }

    /// <summary>A line (era) of a "Zone" definition.</summary>
    public sealed class TzZoneLine
    {
        public TimeSpan StandardOffset { get; internal set; }
        /// <summary>Name of the rule set, or null when none / fixed save.</summary>
        public string RulesName { get; internal set; }
        public TimeSpan FixedSave { get; internal set; }
        public string Format { get; internal set; }
        /// <summary>Local until time as written in the file, or null when open-ended.</summary>
        public DateTime? UntilLocal { get; internal set; }
        /// <summary>'w' wall clock, 's' standard time, 'u' UTC.</summary>
        public char UntilType { get; internal set; }
    }

    public sealed class TzZone
    {
        public string Name { get; internal set; }
        public List<TzZoneLine> Lines { get; } = new List<TzZoneLine>();
    }

    /// <summary>Parsed contents of the tz source files.</summary>
    public sealed class TzData
    {
        public Dictionary<string, List<TzRule>> Rules { get; } = new Dictionary<string, List<TzRule>>(StringComparer.Ordinal);
        public Dictionary<string, TzZone> Zones { get; } = new Dictionary<string, TzZone>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Alias name to link target.</summary>
        public Dictionary<string, string> Links { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The rule in effect for a zone at a given instant.</summary>
    public sealed class TzRuleInfo
    {
        public string ZoneId { get; internal set; }
        public string CanonicalId { get; internal set; }
        public TimeSpan StandardOffset { get; internal set; }
        public TimeSpan SaveOffset { get; internal set; }
        public TimeSpan TotalOffset { get { return StandardOffset + SaveOffset; } }
        public bool IsDaylightSaving { get { return SaveOffset != TimeSpan.Zero; } }
        public string Abbreviation { get; internal set; }
        public string Format { get; internal set; }
        public string RuleName { get; internal set; }
        public string Letter { get; internal set; }
        /// <summary>Start of the period this info is valid for (null if unbounded).</summary>
        public DateTimeOffset? ValidFrom { get; internal set; }
        /// <summary>End of the period (the "until" boundary), null if unbounded.</summary>
        public DateTimeOffset? ValidUntil { get; internal set; }
    }
}
