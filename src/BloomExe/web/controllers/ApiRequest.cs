using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.Utils;
using Newtonsoft.Json;

namespace Bloom.Api
{
    public delegate void EndpointHandler(ApiRequest request);

    public abstract class BaseEndpointRegistration
    {
        public bool HandleOnUIThread = true;
        public bool RequiresSync = true; // set false if handler does its own thread-handling.
        public bool DoMeasure = false;
        public string MeasurementLabel;
        public Func<string> FunctionToGetLabel;

        public void Measureable(string label = null)
        {
            DoMeasure = true;
            if (label != null) // otherwise, stick with our current value which comes from the url
                MeasurementLabel = label;
        }

        public void Measureable(Func<string> getLabel)
        {
            DoMeasure = true;
            FunctionToGetLabel = getLabel;
        }

        public abstract Task Handle(ApiRequest request);
    }

    public class EndpointRegistration : BaseEndpointRegistration
    {
        public EndpointHandler Handler;

        public override async Task Handle(ApiRequest request)
        {
            Handler(request);
        }
    }

    public class AsyncEndpointRegistration : BaseEndpointRegistration
    {
        public Func<ApiRequest, Task> Handler;

        public override async Task Handle(ApiRequest request)
        {
            await Handler(request);
        }
    }

    /// <summary>
    /// When the Bloom UI makes an API call, a method that has been registered to handle that
    /// endpoint is called and given one of these. That method uses this class to get information
    /// on the request, and also to reply to the caller.
    /// </summary>
    /// <remarks>The goal here is to reduce code while increasing clarity and error catching.</remarks>
    public class ApiRequest
    {
        private readonly IRequestInfo _requestInfo;
        public readonly CollectionSettings CurrentCollectionSettings;
        public readonly Book.Book CurrentBook;
        public NameValueCollection Parameters;

        public ApiRequest(
            IRequestInfo requestinfo,
            CollectionSettings currentCollectionSettings,
            Book.Book currentBook
        )
        {
            _requestInfo = requestinfo;
            CurrentCollectionSettings = currentCollectionSettings;
            CurrentBook = currentBook;
            Parameters = requestinfo.GetQueryParameters();
        }

        /// <summary>
        /// Get the actual local path that the server would retrieve given a Bloom URL
        /// that ends up at a local file. For now it is mainly useful for things in the book folder; it doesn't have
        /// all the smarts to locate files shipped with the application, it is just concerned with reversing
        /// the various tricks we use to encode paths as URLs.
        /// </summary>
        public string LocalPath()
        {
            return BloomServer.GetLocalPathWithoutQuery(this._requestInfo);
        }

        public HttpMethods HttpMethod
        {
            get { return _requestInfo.HttpMethod; }
        }

        /// <summary>
        /// This is safe to use with axios.Post. See BL-4901. There, not returning any text at all
        /// caused some kind of problem in axios.post(), after the screen had been shut down.
        /// </summary>
        public void PostSucceeded()
        {
            _requestInfo.ResponseContentType = "text/plain";
            _requestInfo.WriteCompleteOutput("OK");
        }

        //Used when an anchor has given us info, but we don't actually want the browser to navigate
        //For example, anchors that lead to help lead to an api handler that opens help but then
        //calls this so that the browser just stays where it was.
        public void ExternalLinkSucceeded()
        {
            _requestInfo.ExternalLinkSucceeded();
        }

        public void ReplyWithText(string text)
        {
            //Debug.WriteLine(this.Requestinfo.LocalPathWithoutQuery + ": " + text);
            _requestInfo.ResponseContentType = "text/plain";
            _requestInfo.WriteCompleteOutput(text);
        }

        public void ReplyWithAudioFileContents(string path)
        {
            _requestInfo.ResponseContentType = path.EndsWith(".mp3") ? "audio/mpeg" : "audio/wav";
            _requestInfo.ReplyWithFileContent(path);
        }

        public void ReplyWithHtml(string html)
        {
            _requestInfo.ResponseContentType = "text/html";
            _requestInfo.WriteCompleteOutput(html);
        }

        public void ReplyWithJson(string json)
        {
            //Debug.WriteLine(this.Requestinfo.LocalPathWithoutQuery + ": " + json);
            _requestInfo.ResponseContentType = "application/json";
            _requestInfo.WriteCompleteOutput(json);
        }

        public void ReplyWithJson(object objectToMakeJson)
        {
            //Debug.WriteLine(this.Requestinfo.LocalPathWithoutQuery + ": " + json);
            _requestInfo.ResponseContentType = "application/json";
            _requestInfo.WriteCompleteOutput(JsonConvert.SerializeObject(objectToMakeJson));
        }

        public void ReplyWithImage(string imagePath)
        {
            _requestInfo.ReplyWithImage(imagePath);
        }

        public void ReplyWithStreamContent(Stream input, string responseType)
        {
            _requestInfo.ReplyWithStreamContent(input, responseType);
        }

        /// <summary>
        /// Use this one in cases where the error has already been output to a progress box,
        /// and repeating the error is just noise.
        /// </summary>
        public void Failed(string text = null)
        {
            Failed(HttpStatusCode.ServiceUnavailable, text);
        }

        public void Failed(HttpStatusCode statusCode, string text = null)
        {
            _requestInfo.ResponseContentType = "text/plain";
            int statusCodeInt = (int)statusCode;
            if (text == null)
            {
                _requestInfo.WriteError(statusCodeInt);
            }
            else
            {
                _requestInfo.WriteError(statusCodeInt, text);
            }
        }

        public static async Task<bool> Handle(
            BaseEndpointRegistration endpointRegistration,
            IRequestInfo info,
            CollectionSettings collectionSettings,
            Book.Book currentBook
        )
        {
            var request = new ApiRequest(info, collectionSettings, currentBook);
            try
            {
                if (Program.RunningUnitTests)
                {
                    await endpointRegistration.Handle(request);
                }
                else
                {
                    var label = "";
                    if (
                        endpointRegistration.DoMeasure
                        && (endpointRegistration.FunctionToGetLabel != null)
                    )
                    {
                        label = endpointRegistration.FunctionToGetLabel();
                    }
                    else if (endpointRegistration.DoMeasure)
                    {
                        label = endpointRegistration.MeasurementLabel;
                    }
                    using (
                        endpointRegistration.DoMeasure
                            ? PerformanceMeasurement.Global?.Measure(label)
                            : null
                    )
                    {
                        // Note: If the user is still interacting with the application, openForms could change and become empty.
                        // We'd prefer the Bloom shell, if it's open, so we concat that list onto the list of any others and take
                        // the last.
                        var forms = Application.OpenForms.Cast<Form>().ToList();
                        var shells = forms.Where(x => x is Bloom.Shell);
                        var formForSynchronizing = forms.Concat(shells).LastOrDefault();
                        if (
                            endpointRegistration.HandleOnUIThread
                            && formForSynchronizing != null
                            && formForSynchronizing.InvokeRequired
                        )
                        {
                            await InvokeWithErrorHandling(
                                endpointRegistration,
                                formForSynchronizing,
                                request
                            );
                        }
                        else
                        {
                            await endpointRegistration.Handle(request);
                        }
                    }
                }
                if (!info.HaveOutput)
                {
                    throw new ApplicationException(
                        $"The EndpointHandler for {info.RawUrl} never called a Succeeded(), Failed(), or ReplyWith() Function."
                    );
                }
            }
            catch (System.IO.IOException e)
            {
                var shortMsg = String.Format(
                    L10NSharp.LocalizationManager.GetDynamicString(
                        "Bloom",
                        "Errors.CannotAccessFile",
                        "Cannot access {0}"
                    ),
                    info.RawUrl
                );
                var longMsg = String.Format(
                    "Bloom could not access {0}.  The file may be open in another program.",
                    info.RawUrl
                );
                NonFatalProblem.Report(ModalIf.None, PassiveIf.All, shortMsg, longMsg, e);
                request.Failed(shortMsg);
                return false;
            }
            catch (Exception e)
            {
                //Hard to reproduce, but I got one of these supertooltip disposal errors in a yellow box
                //while switching between publish tabs (e.g. /bloom/api/publish/bloompub/cleanup).
                //I don't think these are worth alarming the user about, so let's be sensitive to what channel we're on.
                NonFatalProblem.Report(
                    ModalIf.Alpha,
                    PassiveIf.All,
                    "Error in " + info.RawUrl,
                    exception: e
                );
                request.Failed("Error in " + info.RawUrl);
                return false;
            }
            return true;
        }

        // If you just Invoke(), the stack trace of any generated exception gets lost.
        // The stacktrace instead just ends with the invoke(), which isn't useful. So here we wrap
        // the call to the handler in a delegate that catches the exception and saves it
        // in our local scope, where we can then use it for error reporting.
        private static async Task<bool> InvokeWithErrorHandling(
            BaseEndpointRegistration endpointRegistration,
            Form formForSynchronizing,
            ApiRequest request
        )
        {
            Exception handlerException = null;

            BloomServer._theOneInstance.RegisterThreadBlocking();

            // This will block until the UI thread is done invoking this.
            await (Task)
                formForSynchronizing.Invoke(
                    new Func<ApiRequest, Task>(
                        async (req) =>
                        {
                            try
                            {
                                await endpointRegistration.Handle(req);
                            }
                            catch (Exception error)
                            {
                                handlerException = error;
                            }
                        }
                    ),
                    request
                );

            BloomServer._theOneInstance.RegisterThreadUnblocked();

            if (handlerException != null)
            {
                ExceptionDispatchInfo.Capture(handlerException).Throw();
            }
            return true;
        }

        public string RequestContentType => _requestInfo.RequestContentType;

        public UrlPathString RequiredFileNameOrPath(string name)
        {
            if (Parameters.AllKeys.Contains(name))
                return UrlPathString.CreateFromUnencodedString(Parameters[name]);
            throw new ApplicationException(
                "The query " + _requestInfo.RawUrl + " should have parameter " + name
            );
        }

        public string GetParamOrNull(string name)
        {
            if (Parameters.AllKeys.Contains(name))
                return Parameters[name];
            return null;
        }

        public string RequiredParam(string name)
        {
            if (Parameters.AllKeys.Contains(name))
                return Parameters[name];
            throw new ApplicationException(
                "The query " + _requestInfo.RawUrl + " should have parameter " + name
            );
        }

        public T RequiredPostObject<T>()
        {
            return JsonConvert.DeserializeObject<T>(RequiredPostJson());
        }

        /// <summary>
        /// Convert the json to an anonymous object such that you can use RequiredPostDynamic().foobar to get foobar of any type.
        /// </summary>
        /// <remarks> Note: Surprisingly,
        ///	string x = request.RequiredPostDynamic().someString;
        ///	var y = request.RequiredPostDynamic().someString;
        ///	x != y, but x == y.Value
        /// </remarks>
        public dynamic RequiredPostDynamic()
        {
            return RequiredPostObject<dynamic>();
        }

        public string RequiredPostJson()
        {
            var json = GetPostJson();
            if (!string.IsNullOrWhiteSpace(json))
            {
                return json;
            }
            throw new ApplicationException(
                "The query " + _requestInfo.RawUrl + " should have post json"
            );
        }

        /// <summary>
        /// Gets the JSON data from a POST request
        /// </summary>
        /// <returns>If the request's content type is "application/json", returns the data as a JSON string
        /// If the request's content type is not "application/json", returns null instead</returns>
        public string GetPostJsonOrNull()
        {
            string contentType = RequestContentType; // Note: could contain suffixes like charset=[...]
            if (contentType == null || !contentType.ToLowerInvariant().Contains("application/json"))
            {
                return null;
            }

            return GetPostJson();
        }

        public string GetPostJson()
        {
            Debug.Assert(
                _requestInfo.HttpMethod == HttpMethods.Post,
                "Expected HttpMethod to be Post but instead got: "
                    + _requestInfo.HttpMethod.ToString()
            );

            return _requestInfo.GetPostJson();
        }

        public string RequiredPostString(bool unescape = true)
        {
            var s = GetPostStringOrNull(unescape);
            if (!string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
            throw new ApplicationException(
                "The query " + _requestInfo.RawUrl + " should have post string"
            );
        }

        public string GetPostStringOrNull(bool unescape = true)
        {
            string contentType = RequestContentType;
            if (contentType == null)
            {
                return null;
            }
            Debug.Assert(_requestInfo.HttpMethod == HttpMethods.Post);
            return _requestInfo.GetPostString(unescape);
        }

        /// <summary>
        /// Get an enum value of type T that was passed as application/json
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <remarks>requires something like this:
        ///    axios.post("api/bloom/foo", myEnum, {
        ///       headers: { "Content-Type": "application/json" }});
        /// </remarks>
        /// <returns>An enum value</returns>
        internal T RequiredPostEnumAsJson<T>()
        {
            Debug.Assert(
                typeof(T).IsEnum,
                "Type passed to RequiredPostEnumAsJson() is not an Enum."
            );
            return (T)Enum.Parse(typeof(T), RequiredPostJson());
        }

        /// <summary>
        /// Get a boolean value that was passed as application/json
        /// </summary>
        /// <remarks>
        /// Used by BloomServer.RegisterBooleanEndpointHandler() and requires something like this:
        ///    axios.post("api/bloom/foo", myBool, {
        ///       headers: { "Content-Type": "application/json" }});
        /// </remarks>
        /// <returns></returns>
        internal bool RequiredPostBooleanAsJson()
        {
            // There isn't an obvious choice for passing a simple true/false, but a plain true/false counts as json:
            // https://tools.ietf.org/html/rfc7493#section-4.1  Note we don't have to be compatible with old parsers. so we can just return true or false
            return RequiredPostJson() == "true";
        }

        // NB: This is probably going to fail if the JSON contains anything that isn't a string.
        public string RequiredPostString(string key)
        {
            Debug.Assert(_requestInfo.HttpMethod == HttpMethods.Post);
            var values = _requestInfo.GetPostDataWhenFormEncoded().GetValues(key);

            if (values != null && values.Length == 1)
            {
                return values[0];
            }
            throw new ApplicationException(
                "The query " + _requestInfo.RawUrl + " should have 1 value for " + key
            );
        }

        public byte[] RawPostData => _requestInfo.GetRawPostData();
        public Stream RawPostStream => _requestInfo.GetRawPostStream();

        public NameValueCollection GetPostDataWhenFormEncoded()
        {
            return _requestInfo.GetPostDataWhenFormEncoded();
        }

        public void ReplyWithBoolean(bool value)
        {
            // https://tools.ietf.org/html/rfc7493#section-4.1  Note we don't have to be compatible with old parsers. so we can just return true or false
            ReplyWithJson(value);
        }

        public void ReplyWithEnum<T>(T value)
        {
            ReplyWithJson(Enum.GetName(typeof(T), value));
        }
    }
}
