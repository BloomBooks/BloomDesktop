using System.IO;
using System.Linq;
using Bloom.web.controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests of the store behind the calendarSettings endpoint: the collection's
    /// configuration.txt.
    /// </summary>
    [TestFixture]
    public class CalendarSettingsApiTests
    {
        private TemporaryFolder _collectionFolder;

        [SetUp]
        public void Setup()
        {
            _collectionFolder = new TemporaryFolder("CalendarSettingsApiTests");
        }

        [TearDown]
        public void TearDown()
        {
            _collectionFolder.Dispose();
        }

        private string ConfigurationFilePath =>
            Path.Combine(_collectionFolder.Path, "configuration.txt");

        [Test]
        public void Read_NoConfigurationFile_GivesEmptyNamesAndNoFirstDay()
        {
            Assert.That(
                RobustFile.Exists(ConfigurationFilePath),
                Is.False,
                "Test setup: the collection should not have a configuration file yet"
            );

            var settings = CalendarSettingsApi.Read(_collectionFolder.Path);

            Assert.That(settings.MonthNames.Length, Is.EqualTo(12));
            Assert.That(settings.DayNames.Length, Is.EqualTo(7));
            Assert.That(settings.MonthNames, Is.All.EqualTo(""));
            Assert.That(settings.DayNames, Is.All.EqualTo(""));
            Assert.That(settings.FirstDayOfWeek, Is.Null);
        }

        [Test]
        public void Read_NoConfigurationFile_JsonHasTheThreeExpectedFields()
        {
            var json = JObject.Parse(
                JsonConvert.SerializeObject(CalendarSettingsApi.Read(_collectionFolder.Path))
            );

            Assert.That(json["monthNames"].Type, Is.EqualTo(JTokenType.Array));
            Assert.That(json["dayNames"].Type, Is.EqualTo(JTokenType.Array));
            Assert.That(json["firstDayOfWeek"].Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void WriteThenRead_GivesBackWhatWeWrote()
        {
            var written = CalendarSettingsApi.Read(_collectionFolder.Path);
            written.MonthNames[0] = "Janvier";
            written.MonthNames[11] = "Décembre";
            written.DayNames[3] = "Mercredi";
            written.FirstDayOfWeek = 1;

            CalendarSettingsApi.Write(_collectionFolder.Path, written);
            var read = CalendarSettingsApi.Read(_collectionFolder.Path);

            Assert.That(read.MonthNames[0], Is.EqualTo("Janvier"));
            Assert.That(read.MonthNames[11], Is.EqualTo("Décembre"));
            Assert.That(read.MonthNames[5], Is.EqualTo(""));
            Assert.That(read.DayNames[3], Is.EqualTo("Mercredi"));
            Assert.That(read.DayNames[0], Is.EqualTo(""));
            Assert.That(read.FirstDayOfWeek, Is.EqualTo(1));
        }

        [Test]
        public void Write_PutsTheCalendarUnderLibraryInConfigurationTxt()
        {
            var settings = CalendarSettingsApi.Read(_collectionFolder.Path);
            settings.MonthNames[0] = "Enero";
            settings.FirstDayOfWeek = 0;

            CalendarSettingsApi.Write(_collectionFolder.Path, settings);

            var root = JObject.Parse(RobustFile.ReadAllText(ConfigurationFilePath));
            Assert.That(
                root["library"]["calendar"]["monthNames"][0].ToString(),
                Is.EqualTo("Enero")
            );
            Assert.That(root["library"]["calendar"]["firstDayOfWeek"].Value<int>(), Is.EqualTo(0));
            Assert.That(root["library"]["calendar"]["dayNames"].Children().Count(), Is.EqualTo(7));
        }

        [Test]
        public void Write_LeavesOtherPartsOfTheFileAlone()
        {
            RobustFile.WriteAllText(
                ConfigurationFilePath,
                @"{'library': {'somethingElse': 'keep me', 'calendar': {'monthNames': ['gone']}}, 'outside': 42}".Replace(
                    '\'',
                    '"'
                )
            );

            var settings = CalendarSettingsApi.Read(_collectionFolder.Path);
            Assert.That(
                settings.MonthNames[0],
                Is.EqualTo("gone"),
                "Test setup: we should have read the value we are about to replace"
            );
            settings.MonthNames[0] = "Janeiro";
            CalendarSettingsApi.Write(_collectionFolder.Path, settings);

            var root = JObject.Parse(RobustFile.ReadAllText(ConfigurationFilePath));
            Assert.That(root["library"]["somethingElse"].ToString(), Is.EqualTo("keep me"));
            Assert.That(root["outside"].Value<int>(), Is.EqualTo(42));
            Assert.That(
                root["library"]["calendar"]["monthNames"][0].ToString(),
                Is.EqualTo("Janeiro")
            );
        }

        [Test]
        public void Read_LegacyFileWithDayAbbreviations_FindsTheDayNames()
        {
            // What the old Wall Calendar setup wizard wrote: the root object IS the library,
            // and the weekday names are called 'dayAbbreviations'.
            RobustFile.WriteAllText(
                ConfigurationFilePath,
                @"{'calendar': {'monthNames': ['January'], 'dayAbbreviations': ['Sun','Mon','Tues','Wed','Thur','Fri','Sat']}}".Replace(
                    '\'',
                    '"'
                )
            );

            var settings = CalendarSettingsApi.Read(_collectionFolder.Path);

            Assert.That(settings.MonthNames[0], Is.EqualTo("January"));
            Assert.That(settings.DayNames[0], Is.EqualTo("Sun"));
            Assert.That(settings.DayNames[2], Is.EqualTo("Tues"));
            Assert.That(settings.DayNames[6], Is.EqualTo("Sat"));
            Assert.That(settings.FirstDayOfWeek, Is.Null);
        }

        [Test]
        public void Read_DayNamesPresent_PrefersThemOverDayAbbreviations()
        {
            RobustFile.WriteAllText(
                ConfigurationFilePath,
                @"{'library': {'calendar': {'dayNames': ['Sunday'], 'dayAbbreviations': ['Sun']}}}".Replace(
                    '\'',
                    '"'
                )
            );

            Assert.That(
                CalendarSettingsApi.Read(_collectionFolder.Path).DayNames[0],
                Is.EqualTo("Sunday")
            );
        }
    }
}
