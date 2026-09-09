using System.Collections.Generic;
using Bloom.Api;
using Bloom.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bloom.web.controllers
{
    /// <summary>
    /// The front end's door to <see cref="ServiceKeyStore"/>: the API keys the user fetched from
    /// some service's own website, such as Pixabay for picture search or OpenRouter for
    /// "Edit with AI". Like the store, this knows nothing about any particular service -- a
    /// caller picks a name -- so a new service needs no change here.
    ///
    /// Two endpoints, because there are two shapes of caller:
    ///
    /// serviceKeys/key handles ONE key by its full name, and its value is the bare string in
    /// both directions. That suits a feature with a single key, such as "Edit with AI".
    ///
    /// serviceKeys/keys handles a whole NAMESPACE at once, named by a prefix, as one flat JSON
    /// object of short name to key plus a "version" property. That suits the image gallery,
    /// which has one key per search provider and hands Bloom all of them together. The prefix
    /// is stripped on the way out and added on the way in, so the gallery deals only in its
    /// own provider ids and neither side changes when it gains a provider.
    ///
    /// These endpoints are registered application-wide rather than per collection, because a
    /// key belongs to the Windows user and outlives any collection. They are not
    /// authenticated, which is true of Bloom's whole localhost API; anything that can reach
    /// the server can read the user's keys, and the server is only reachable from this
    /// computer.
    /// </summary>
    public class ServiceKeysApi
    {
        /// <summary>
        /// The property the namespace shape carries alongside the keys, so that the front end
        /// can tell a Bloom that speaks this shape from a later one that does not.
        /// </summary>
        private const int kNamespaceFormatVersion = 1;

        private const string kVersionPropertyName = "version";

        /// <summary>Wires up the endpoints. See the class comment.</summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("serviceKeys/key", HandleKey, false);
            apiHandler.RegisterEndpointHandler("serviceKeys/keys", HandleNamespace, false);
        }

        /// <summary>
        /// GET returns the one key named by the "name" parameter as plain text, the same bare
        /// string a POST sends. The body is empty when there is no such key, and also when
        /// there is one this computer cannot decrypt -- which the caller should treat the same
        /// way, by asking the user for the key again.
        /// POST stores the posted body as that key's value; an empty body removes it.
        /// </summary>
        private void HandleKey(ApiRequest request)
        {
            var name = request.RequiredParam("name");
            if (request.HttpMethod == HttpMethods.Get)
            {
                request.ReplyWithText(ServiceKeyStore.Get(name) ?? "");
                return;
            }
            // A key can hold any character a service cares to use, "+" and "%" among them, so
            // take the body exactly as it was posted. The default unescape would turn a "+"
            // into a space and decode a percent escape, and Bloom would store a key the
            // service then rejects.
            ServiceKeyStore.Set(name, request.RequiredPostString(unescape: false));
            request.PostSucceeded();
        }

        /// <summary>
        /// GET returns every readable key whose name starts with the "prefix" parameter, as
        /// one flat object of the rest-of-the-name to the key, plus the format version.
        /// POST replaces that namespace with the JSON that is posted, in the same shape.
        /// </summary>
        private void HandleNamespace(ApiRequest request)
        {
            var prefix = request.RequiredParam("prefix");
            if (request.HttpMethod == HttpMethods.Get)
            {
                var keys = new Dictionary<string, object>
                {
                    [kVersionPropertyName] = kNamespaceFormatVersion,
                };
                foreach (var name in ServiceKeyStore.GetNames(prefix))
                {
                    // Null when the key cannot be decrypted on this computer, which a caller
                    // reads the same way as never having had a key: it asks for one.
                    var key = ServiceKeyStore.Get(name);
                    if (!string.IsNullOrEmpty(key))
                        keys[name.Substring(prefix.Length)] = key;
                }
                request.ReplyWithJson(JsonConvert.SerializeObject(keys));
                return;
            }

            var posted = JObject.Parse(request.RequiredPostJson());
            var postedShortNames = new HashSet<string>();
            foreach (var property in posted.Properties())
            {
                if (property.Name == kVersionPropertyName)
                    continue;
                postedShortNames.Add(property.Name);
                ServiceKeyStore.Set(prefix + property.Name, (string)property.Value);
            }

            // The caller sends the whole namespace, so a name missing from the post is a key
            // the user removed. A key this version cannot read is a different case: it never
            // reached the caller, so its absence from the post says nothing about what the
            // user wants, and deleting it would throw away a key a newer Bloom put there.
            foreach (var name in ServiceKeyStore.GetNames(prefix))
            {
                if (postedShortNames.Contains(name.Substring(prefix.Length)))
                    continue;
                if (!ServiceKeyStore.CanRead(name))
                    continue;
                ServiceKeyStore.Set(name, null);
            }
            request.PostSucceeded();
        }
    }
}
