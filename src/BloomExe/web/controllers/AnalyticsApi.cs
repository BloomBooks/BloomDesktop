using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Bloom.Api;
using Bloom.Book;
using Bloom.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.Reporting;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Lets front-end code report an analytics event. Until this existed, every one of our analytics
    /// events came from C#, so a feature implemented in React could only be measured by inventing a
    /// bespoke endpoint for it (as publish/pdf/printAnalytics still does) -- which in practice meant
    /// most of them were not measured at all.
    /// </summary>
    public class AnalyticsApi
    {
        private readonly BookSelection _bookSelection;

        public AnalyticsApi(BookSelection bookSelection)
        {
            _bookSelection = bookSelection;
        }

        /// <summary>
        /// Register the one endpoint. This is project-level (see ProjectContext) rather than
        /// application-level so that we can fill in the current book's id; every caller so far is
        /// edit-tab code, which always has a project open. A future need for analytics from the
        /// collection chooser would have to revisit that.
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // requiresSync: false because sending an event neither reads nor writes anything else in
            // Bloom, so there is no reason to make it queue behind the rest of the API.
            apiHandler.RegisterEndpointHandler("analytics/track", HandleTrack, false, false);
        }

        /// <summary>
        /// POST analytics/track with {"event": "Some Event", "properties": {...}}. Property values may
        /// be strings, numbers or booleans; they all reach Segment as strings, which is what
        /// DesktopAnalytics accepts.
        /// </summary>
        private void HandleTrack(ApiRequest request)
        {
            var body = ParseTrackRequestBody(request.RequiredPostJson());
            var eventName = body["event"]?.ToString();
            if (string.IsNullOrWhiteSpace(eventName))
            {
                // This is a definite programming error in the caller, not a runtime condition, and
                // it fails silently in the worst way: nothing is recorded and nothing says so.
                //
                // BloomDebug.Fail stops a developer dead over it -- breaking into the debugger,
                // failing the test, or putting up a dialog, whichever suits where Bloom is running
                // -- and compiles away entirely in a release build, where the log line and the
                // failed request are what remain. We do not use Debug.Fail directly because with
                // no debugger attached it is Environment.FailFast; see BloomDebug.
                const string complaint =
                    "[analytics] analytics/track was called with no event name";
                BloomDebug.Fail(complaint);
                Logger.WriteEvent(complaint);
                Console.Error.WriteLine(complaint);
                request.Failed("analytics/track requires an 'event' name");
                return;
            }
            var properties = GetProperties(body["properties"] as JObject);
            // Supply BookId here rather than making every caller find it: most of our C# events
            // already carry it, and front-end code in the edit view generally has no idea what the
            // current book's id is. A caller that knows better (e.g. it is reporting about some
            // other book) can pass its own and we leave it alone.
            //
            // Do NOT add a branding property here, tempting though it is. Every event already
            // carries one, as "BrandingProjectName": CollectionSettings.SetAnalyticsProperties
            // hands it to DesktopAnalytics as an application property, and those ride on every
            // subsequent event. It is also the better value -- the subscription descriptor, which
            // encodes tier, flavor and subscriber, where Subscription.BrandingKey normalizes all
            // three away.
            //
            // The trap: SetAnalyticsProperties returns early when tracking is off, and the log line
            // shows only per-event properties, so on a developer build no branding appears
            // anywhere. That is not evidence it is missing in production.
            var book = _bookSelection?.CurrentSelection;
            if (book != null && !properties.ContainsKey("BookId"))
                properties["BookId"] = book.ID;
            BloomAnalytics.Track(eventName, properties);
            request.PostSucceeded();
        }

        /// <summary>
        /// Report that a picture in a book was replaced, saying where it came from.
        ///
        /// The count on its own is not the interesting part -- choosing a picture is an essential
        /// thing to do and we already know people do it. The source is: it turns a number we don't
        /// need into the breakdown of picture sources that we do. Which is also why this lives in
        /// one place: the event has several call sites and would be worthless if they disagreed
        /// about the vocabulary. This is the one place for the routes that report from here -- a
        /// paste, the image chooser, a file from disk. The AI image editor route reports from the
        /// browser instead, because only the browser knows whether a picture on the page being
        /// edited actually landed (see AiImageEditorApi.HandleCommit), so it uses the mirror of
        /// this method: trackChangePicture in bloomApi.ts. Change the words here, change them
        /// there.
        /// </summary>
        /// <param name="source">Where the picture came from: an image-gallery ISearchProvider id
        /// ("pixabay", "openverse", or a local collection's slug such as "art-of-reading"),
        /// "local-disk", "clipboard", or "ai-editor".
        ///
        /// One field rather than the route and the provider separately, because every provider
        /// worth telling apart belonged to a single route -- the image chooser -- so each is now a
        /// first-class source in its own right, and the two routes that had no provider of their
        /// own (a paste, a file off disk) name themselves the same way.</param>
        public static void TrackChangePicture(string source, string bookId)
        {
            BloomAnalytics.Track(
                "Change Picture",
                new Dictionary<string, string>
                {
                    { "source", source ?? "unknown" },
                    { "BookId", bookId ?? "" },
                }
            );
        }

        /// <summary>
        /// Parse the request body WITHOUT letting Newtonsoft turn date-looking strings into dates.
        ///
        /// Its default DateParseHandling materializes a JSON string that parses as a date-time --
        /// "2024-01-01T10:00:00Z", say -- as a DateTime rather than a string. We would then record
        /// whatever the local culture makes of it: "1/1/2024 10:00:00 AM" on this machine, something
        /// else on the next. (A date-only "2024-01-01" is left alone, so the reachable case is a full
        /// timestamp.) Property values arrive as JSON strings and must reach Segment exactly as the
        /// caller sent them: we do not control the shape of all of them -- a search provider's error
        /// message, a model name, an id from someone else's API -- and a value we silently rewrite is
        /// a record that disagrees with what happened.
        /// </summary>
        internal static JObject ParseTrackRequestBody(string json)
        {
            using (
                var reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                }
            )
            {
                return JObject.Load(reader);
            }
        }

        /// <summary>
        /// Flatten the properties object into the string dictionary DesktopAnalytics wants. Null and
        /// undefined values are dropped rather than sent as empty strings, so a caller can pass an
        /// optional property without having to decide whether to include the key at all.
        /// </summary>
        internal static Dictionary<string, string> GetProperties(JObject properties)
        {
            var result = new Dictionary<string, string>();
            if (properties == null)
                return result;
            foreach (var property in properties)
            {
                var value = property.Value;
                switch (value?.Type)
                {
                    case null:
                    case JTokenType.Null:
                    case JTokenType.Undefined:
                        continue;
                    case JTokenType.Boolean:
                        // JValue.ToString() would give us "True"; we want what the JSON said.
                        result[property.Key] = (bool)value ? "true" : "false";
                        break;
                    case JTokenType.Integer:
                        result[property.Key] = ((long)value).ToString(CultureInfo.InvariantCulture);
                        break;
                    case JTokenType.Float:
                        // Invariant so that a user in a comma-decimal locale doesn't send us "1,5".
                        result[property.Key] = ((double)value).ToString(
                            CultureInfo.InvariantCulture
                        );
                        break;
                    default:
                        result[property.Key] = value.ToString();
                        break;
                }
            }
            return result;
        }
    }
}
