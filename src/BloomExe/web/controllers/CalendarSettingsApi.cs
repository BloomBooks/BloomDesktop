using System.IO;
using Bloom.Api;
using Bloom.Collection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.IO;

namespace Bloom.web.controllers
{
    /// <summary>
    /// The month names, weekday names, and first day of the week that the Wall Calendar
    /// tooling remembers for a collection. This is the only part of Bloom's C# that knows
    /// calendars exist; everything else about a calendar book is done by the front end.
    /// </summary>
    public class CalendarSettings
    {
        /// <summary>
        /// Twelve names, January first. An entry is an empty string if the user has not
        /// typed that month's name yet.
        /// </summary>
        [JsonProperty("monthNames")]
        public string[] MonthNames { get; set; }

        /// <summary>
        /// Seven names, Sunday first, whatever the collection's first day of the week is.
        /// An entry is an empty string if the user has not typed that day's name yet.
        /// </summary>
        [JsonProperty("dayNames")]
        public string[] DayNames { get; set; }

        /// <summary>
        /// 0 for Sunday through 6 for Saturday, or null if the user has not chosen one.
        /// </summary>
        [JsonProperty("firstDayOfWeek")]
        public int? FirstDayOfWeek { get; set; }
    }

    /// <summary>
    /// Reads and writes the collection's calendar settings for the front-end calendar
    /// tooling. They live in the collection folder's configuration.txt, which is where the
    /// old Wall Calendar setup wizard kept them.
    /// </summary>
    public class CalendarSettingsApi
    {
        private readonly CollectionSettings _collectionSettings;

        // Called by autofac, which creates the one instance and registers it with the server.
        public CalendarSettingsApi(CollectionSettings collectionSettings)
        {
            _collectionSettings = collectionSettings;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("calendarSettings", HandleCalendarSettings, false);
        }

        private void HandleCalendarSettings(ApiRequest request)
        {
            switch (request.HttpMethod)
            {
                case HttpMethods.Get:
                    request.ReplyWithJson(
                        JsonConvert.SerializeObject(Read(_collectionSettings.FolderPath))
                    );
                    break;
                case HttpMethods.Post:
                    Write(
                        _collectionSettings.FolderPath,
                        JsonConvert.DeserializeObject<CalendarSettings>(request.RequiredPostJson())
                    );
                    request.PostSucceeded();
                    break;
            }
        }

        /// <summary>
        /// The file the settings live in. It holds more than the calendar: it is the
        /// collection's whole "library" configuration blob.
        /// </summary>
        public static string GetConfigurationFilePath(string collectionFolder)
        {
            return Path.Combine(collectionFolder, "configuration.txt");
        }

        /// <summary>
        /// The calendar settings of the given collection. A missing file, or a file with no
        /// calendar section, yields empty names and no first day of the week.
        /// </summary>
        public static CalendarSettings Read(string collectionFolder)
        {
            var settings = new CalendarSettings
            {
                MonthNames = new string[12],
                DayNames = new string[7],
                FirstDayOfWeek = null,
            };
            for (var i = 0; i < settings.MonthNames.Length; i++)
                settings.MonthNames[i] = "";
            for (var i = 0; i < settings.DayNames.Length; i++)
                settings.DayNames[i] = "";

            var calendar = GetCalendarObject(ReadRoot(collectionFolder));
            if (calendar == null)
                return settings;

            CopyNames(calendar["monthNames"] as JArray, settings.MonthNames);
            // Bloom 6.4 and earlier wrote the weekday names under 'dayAbbreviations'.
            CopyNames(
                (calendar["dayNames"] ?? calendar["dayAbbreviations"]) as JArray,
                settings.DayNames
            );
            var firstDay = calendar["firstDayOfWeek"];
            if (firstDay != null && firstDay.Type != JTokenType.Null)
                settings.FirstDayOfWeek = firstDay.Value<int>();
            return settings;
        }

        /// <summary>
        /// Replaces the calendar section of the collection's configuration file, leaving
        /// anything else in the file alone.
        /// </summary>
        public static void Write(string collectionFolder, CalendarSettings settings)
        {
            var root = ReadRoot(collectionFolder) ?? new JObject();
            if (!(root["library"] is JObject library))
            {
                library = new JObject();
                root["library"] = library;
            }
            library["calendar"] = JObject.FromObject(settings);
            RobustFile.WriteAllText(
                GetConfigurationFilePath(collectionFolder),
                root.ToString(Formatting.Indented)
            );
        }

        /// <summary>
        /// The whole configuration file as JSON, or null if there isn't one we can read.
        /// </summary>
        private static JObject ReadRoot(string collectionFolder)
        {
            var path = GetConfigurationFilePath(collectionFolder);
            if (!RobustFile.Exists(path))
                return null;
            var text = RobustFile.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return null;
            return JObject.Parse(text);
        }

        /// <summary>
        /// The calendar section, wherever it is. We write it inside 'library', but the old
        /// setup wizard wrote a file whose root object was the library itself, and those
        /// files are still out there in people's collections.
        /// </summary>
        private static JObject GetCalendarObject(JObject root)
        {
            if (root == null)
                return null;
            if (root["library"] is JObject library)
                return library["calendar"] as JObject;
            return root["calendar"] as JObject;
        }

        private static void CopyNames(JArray source, string[] destination)
        {
            if (source == null)
                return;
            for (var i = 0; i < destination.Length && i < source.Count; i++)
                destination[i] = source[i]?.ToString() ?? "";
        }
    }
}
