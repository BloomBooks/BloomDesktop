using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.MiscUI;
using Bloom.Utils;
using L10NSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.Reporting;

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
            var secret = request.RequiredPostString(unescape: false);
            if (TrySave(request, () => ServiceKeyStore.Set(name, secret)))
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
            // Gathered as one set and applied in a single write, so a failure part way cannot
            // store some of the user's keys and drop the rest.
            var changes = new List<KeyValuePair<string, string>>();
            foreach (var property in posted.Properties())
            {
                if (property.Name == kVersionPropertyName)
                    continue;
                postedShortNames.Add(property.Name);
                changes.Add(
                    new KeyValuePair<string, string>(prefix + property.Name, (string)property.Value)
                );
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
                changes.Add(new KeyValuePair<string, string>(name, null));
            }

            if (TrySave(request, () => ServiceKeyStore.SetMany(changes)))
                request.PostSucceeded();
        }

        /// <summary>
        /// Makes a change to the store, and turns a failure to write the file into something
        /// the user can act on. <see cref="ServiceKeyStore"/> deliberately throws rather than
        /// pretend a key was saved, and without this the generic API error handler would say
        /// only "Error in /bloom/api/serviceKeys/keys?prefix=...", which tells the user nothing
        /// they can do anything about (BL-16820). The usual causes -- the file is read-only, or
        /// some other program has it open -- are ones only the user can clear, and they cannot
        /// clear them without being told which file it is, so the message names the path.
        /// Returns false when the change did not happen, having already replied to the request.
        /// Only failures to reach the file are caught: anything else -- a failure to encrypt,
        /// say -- is unexpected, and belongs in the generic API handler, which reports it to us
        /// rather than telling the user to go looking at file permissions.
        /// </summary>
        private static bool TrySave(ApiRequest request, Action change)
        {
            try
            {
                change();
                return true;
            }
            catch (Exception error)
                when (error is IOException || error is UnauthorizedAccessException)
            {
                var message = string.Format(
                    LocalizationManager.GetString(
                        "Errors.CannotSaveServiceKey",
                        "Bloom could not save the key you entered, because it could not update this file: {0}. The file may be read-only, or another program may have it open.",
                        "{0} is the full path of a file."
                    ),
                    ServiceKeyStore.FilePath
                );
                // Not shown to the user: the permission and antivirus details that local tech
                // support needs to work out what is holding the file.
                Logger.WriteError(
                    MiscUtils.GetExtendedFileCopyErrorInformation(
                        ServiceKeyStore.FilePath,
                        "Could not save a service key to " + ServiceKeyStore.FilePath
                    ),
                    error
                );
                // Report after a delay, so this API call can finish first: the problem dialog
                // is itself served by this server.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    NonFatalProblem.Report(
                        ModalIf.All,
                        PassiveIf.None,
                        message,
                        error.Message,
                        error,
                        showSendReport: false
                    );
                });
                request.Failed(message);
                return false;
            }
        }
    }
}
