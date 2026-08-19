using System.Collections.Generic;
using System.Globalization;
using Bloom.Api;
using Bloom.Book;
using Newtonsoft.Json.Linq;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Lets front-end code report an analytics event. Until this existed, every one of our analytics
    /// events came from C#, so a feature implemented in React could only be measured by inventing a
    /// bespoke endpoint for it (as publish/pdf/printAnalytics did) -- which in practice meant most of
    /// them were not measured at all.
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
        /// application-level so that we can fill in the current book's id and the collection's
        /// branding; every caller so far is edit-tab code, which always has a project open. A
        /// future need for analytics from the collection chooser would have to revisit that.
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
            var body = JObject.Parse(request.RequiredPostJson());
            var eventName = body["event"]?.ToString();
            if (string.IsNullOrWhiteSpace(eventName))
            {
                // A caller that got the shape of the request wrong should hear about it rather than
                // quietly recording nothing, which is a very hard thing to notice.
                request.Failed("analytics/track requires an 'event' name");
                return;
            }
            var properties = GetProperties(body["properties"] as JObject);
            // Supply BookId and branding here rather than making every caller find them. Most of
            // our C# events already carry BookId, and front-end code in the edit view generally has
            // no idea what the current book's id -- let alone the collection's branding -- is. Yet
            // almost every question we want to ask of these events is worth asking per project:
            // which sources one customer's users search, which gate turned them away. A caller that
            // knows better (e.g. it is reporting about some other book) can pass its own values and
            // we leave them alone.
            var book = _bookSelection?.CurrentSelection;
            if (book != null)
            {
                if (!properties.ContainsKey("BookId"))
                    properties["BookId"] = book.ID;
                if (!properties.ContainsKey("branding"))
                    properties["branding"] = book.CollectionSettings.Subscription.BrandingKey;
            }
            BloomAnalytics.Track(eventName, properties);
            request.PostSucceeded();
        }

        /// <summary>
        /// Report that a picture in a book was replaced, saying which route the user took and where
        /// the bytes came from.
        ///
        /// The count on its own is not the interesting part -- choosing a picture is an essential
        /// thing to do and we already know people do it. These two properties are: they turn a number
        /// we don't need into the breakdown of picture sources that we do. Which is also why this
        /// lives in one place: the event has several call sites and would be worthless if they
        /// disagreed about the vocabulary. This is the one place for the routes that report from
        /// here -- a paste, the image chooser, a file from disk. The AI image editor route reports
        /// from the browser instead, because only the browser knows whether a picture on the page
        /// being edited actually landed (see AiImageEditorApi.HandleCommit), so it uses the mirror
        /// of this method: trackChangePicture in bloomApi.ts. Change the words here, change them
        /// there.
        /// </summary>
        /// <param name="source">The route the user took: "image chooser", "local disk", "paste" or
        /// "AI editor". These categories all existed before 6.5, so the split stays comparable
        /// across the release.</param>
        /// <param name="provider">Where the bytes came from: an image-gallery ISearchProvider id
        /// ("pixabay", "openverse", or a local collection's slug such as "art-of-reading"),
        /// "local-disk", "clipboard", or "ai-editor". Set on every event, so grouping by provider
        /// alone answers "which sources are earning their place".</param>
        public static void TrackChangePicture(string source, string provider, string bookId)
        {
            BloomAnalytics.Track(
                "Change Picture",
                new Dictionary<string, string>
                {
                    { "source", source },
                    { "provider", provider ?? "unknown" },
                    { "BookId", bookId ?? "" },
                }
            );
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
