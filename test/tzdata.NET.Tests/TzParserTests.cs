using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tzdata.Tests
{
    [TestClass]
    public class TzParserTests
    {
        [TestMethod]
        public void Parse_ReadsRulesZonesAndLinks()
        {
            var data = TzParser.Parse(new[] { TestData.Sample });

            Assert.AreEqual(1, data.Rules.Count);
            Assert.AreEqual(3, data.Rules["US"].Count);
            Assert.AreEqual(7, data.Zones.Count);
            Assert.AreEqual(2, data.Links.Count);
            Assert.AreEqual("America/New_York", data.Links["US/Eastern"]);
            Assert.AreEqual("US/Eastern", data.Links["US/Eastern2"]);
        }

        [TestMethod]
        public void Parse_MergesMultipleFiles()
        {
            var data = TzParser.Parse(new[] { "Zone A/One 0 - UTC", "Zone A/Two 1:00 - CET\nLink A/Two A/Alias" });

            Assert.AreEqual(2, data.Zones.Count);
            Assert.AreEqual("A/Two", data.Links["A/Alias"]);
        }

        [TestMethod]
        public void Parse_EmptyInput_YieldsEmptyData()
        {
            var data = TzParser.Parse(new[] { "", "# only comments\n\n" });

            Assert.AreEqual(0, data.Rules.Count);
            Assert.AreEqual(0, data.Zones.Count);
            Assert.AreEqual(0, data.Links.Count);
        }

        [TestMethod]
        public void ParseInto_AddsToExistingData()
        {
            var data = new TzData();
            TzParser.ParseInto(data, "Zone A/One 0 - UTC");
            TzParser.ParseInto(data, "Zone A/Two 0 - UTC");

            Assert.AreEqual(2, data.Zones.Count);
        }

        [TestMethod]
        public void Parse_RuleFields()
        {
            var data = TzParser.Parse(new[] { TestData.Sample });
            var rule = data.Rules["US"][1];

            Assert.AreEqual("US", rule.Name);
            Assert.AreEqual(2007, rule.FromYear);
            Assert.AreEqual(int.MaxValue, rule.ToYear);
            Assert.AreEqual(3, rule.Month);
            Assert.AreEqual(TimeSpan.FromHours(2), rule.AtTime);
            Assert.AreEqual('w', rule.AtType);
            Assert.AreEqual(TimeSpan.FromHours(1), rule.Save);
            Assert.AreEqual("D", rule.Letter);
        }

        [TestMethod]
        public void Parse_RuleWithYearRangeAndDashLetter()
        {
            var data = TzParser.Parse(new[] { "Rule R 1990 1995 - Jan 1 0:30s 0:30 -\nRule R 2000 only - Feb 2 1:00u 0 -" });

            var first = data.Rules["R"][0];
            Assert.AreEqual(1990, first.FromYear);
            Assert.AreEqual(1995, first.ToYear);
            Assert.AreEqual(TimeSpan.FromMinutes(30), first.AtTime);
            Assert.AreEqual('s', first.AtType);
            Assert.AreEqual(TimeSpan.FromMinutes(30), first.Save);
            Assert.AreEqual("", first.Letter);

            var second = data.Rules["R"][1];
            Assert.AreEqual(2000, second.FromYear);
            Assert.AreEqual(2000, second.ToYear);
            Assert.AreEqual('u', second.AtType);
        }

        [TestMethod]
        public void Parse_ZoneLineWithRules()
        {
            var data = TzParser.Parse(new[] { TestData.Sample });
            var line = data.Zones["America/New_York"].Lines[0];

            Assert.AreEqual(TimeSpan.FromHours(-5), line.StandardOffset);
            Assert.AreEqual("US", line.RulesName);
            Assert.AreEqual("E%sT", line.Format);
            Assert.IsNull(line.UntilLocal);
        }

        [TestMethod]
        public void Parse_ZoneWithoutRules()
        {
            var data = TzParser.Parse(new[] { TestData.Sample });
            var line = data.Zones["Etc/UTC"].Lines[0];

            Assert.IsNull(line.RulesName);
            Assert.AreEqual(TimeSpan.Zero, line.FixedSave);
            Assert.AreEqual(TimeSpan.Zero, line.StandardOffset);
        }

        [TestMethod]
        public void Parse_ZoneWithFixedSave()
        {
            var data = TzParser.Parse(new[] { TestData.Sample });
            var line = data.Zones["Test/FixedDst"].Lines[0];

            Assert.IsNull(line.RulesName);
            Assert.AreEqual(TimeSpan.FromHours(1), line.FixedSave);
        }

        [TestMethod]
        public void Parse_NegativeFractionalOffset()
        {
            var data = TzParser.Parse(new[] { TestData.Sample });

            Assert.AreEqual(TimeSpan.FromMinutes(-210), data.Zones["Test/Offset"].Lines[0].StandardOffset);
        }

        [TestMethod]
        public void Parse_ContinuationLinesAndUntil()
        {
            var data = TzParser.Parse(new[] { TestData.Sample });
            var lines = data.Zones["Test/Eras"].Lines;

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(new DateTime(2000, 1, 1), lines[0].UntilLocal);
            Assert.AreEqual('w', lines[0].UntilType);
            Assert.AreEqual("EET", lines[1].Format);
            Assert.IsNull(lines[1].UntilLocal);
        }

        [TestMethod]
        public void Parse_UntilWithTimeAndUtcSuffix()
        {
            var data = TzParser.Parse(new[] { TestData.Sample });
            var line = data.Zones["Test/UntilU"].Lines[0];

            Assert.AreEqual(new DateTime(2000, 1, 1, 2, 0, 0), line.UntilLocal);
            Assert.AreEqual('u', line.UntilType);
        }

        [TestMethod]
        public void Parse_UntilWithLastWeekdayAndWeekdayOnOrAfter()
        {
            var data = TzParser.Parse(new[]
            {
                "Zone A/Last 0 - UTC 2020 Mar lastSun 1:00s\n\t1 - CET",
                "Zone A/After 0 - UTC 2020 Mar Sun>=8\n\t1 - CET",
                "Zone A/Before 0 - UTC 2020 Mar Fri<=15\n\t1 - CET"
            });

            Assert.AreEqual(new DateTime(2020, 3, 29, 1, 0, 0), data.Zones["A/Last"].Lines[0].UntilLocal);
            Assert.AreEqual('s', data.Zones["A/Last"].Lines[0].UntilType);
            Assert.AreEqual(new DateTime(2020, 3, 8), data.Zones["A/After"].Lines[0].UntilLocal);
            Assert.AreEqual(new DateTime(2020, 3, 13), data.Zones["A/Before"].Lines[0].UntilLocal);
        }

        [TestMethod]
        public void Parse_ZoneNamesAreCaseInsensitive()
        {
            var data = TzParser.Parse(new[] { TestData.Sample });

            Assert.IsTrue(data.Zones.ContainsKey("america/new_york"));
            Assert.IsTrue(data.Links.ContainsKey("us/eastern"));
        }

        [TestMethod]
        public void Parse_InvalidMonth_Throws()
        {
            Assert.ThrowsExactly<FormatException>(() =>
                TzParser.Parse(new[] { "Rule R 2000 only - Foo 1 0:00 0 -" }));
        }
    }
}
