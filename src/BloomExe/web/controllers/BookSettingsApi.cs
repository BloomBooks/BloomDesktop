using System;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Book;
using Bloom.web.controllers;
using Newtonsoft.Json;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Api
{
    /// <summary>
    /// Exposes some settings of the current Book via API
    /// </summary>
    public class BookSettingsApi
    {
        private readonly BookSelection _bookSelection;
        private readonly PageRefreshEvent _pageRefreshEvent;
        private readonly BookRefreshEvent _bookRefreshEvent;

        public BookSettingsApi(
            BookSelection bookSelection,
            PageRefreshEvent pageRefreshEvent,
            BookRefreshEvent bookRefreshEvent
        )
        {
            _bookSelection = bookSelection;
            _pageRefreshEvent = pageRefreshEvent;
            _bookRefreshEvent = bookRefreshEvent;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Not sure this needs UI thread, but it can result in saving the page, which seems
            // safest to do that way.
            apiHandler.RegisterEndpointHandler(
                "book/settings",
                HandleBookSettings,
                true /* review */
            );
            apiHandler.RegisterEndpointHandler(
                "book/settings/appearanceUIOptions",
                HandleGetAvailableAppearanceUIOptions,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "book/settings/deleteCustomBookStyles",
                HandleDeleteCustomBookStyles,
                false
            );
        }

        private void HandleDeleteCustomBookStyles(ApiRequest request)
        {
            RobustFile.Delete(
                Path.Combine(_bookSelection.CurrentSelection.FolderPath, "customBookStyles.css")
            );
            _bookSelection.CurrentSelection.SettingsUpdated();
            // We should only delete it when it's not in use, so we should not need to refresh the page.
            IndicatorInfoApi.NotifyIndicatorInfoChanged();
            request.PostSucceeded();
        }

        private void HandleGetAvailableAppearanceUIOptions(ApiRequest request)
        {
            request.ReplyWithJson(
                _bookSelection.CurrentSelection.BookInfo.AppearanceSettings.AppearanceUIOptions
            );
        }

        /// <summary>
        /// Get a json of the book's settings.
        /// </summary>
        private void HandleBookSettings(ApiRequest request)
        {
            switch (request.HttpMethod)
            {
                case HttpMethods.Get:
                    var settings = new
                    {
                        currentToolBoxTool = _bookSelection.CurrentSelection.BookInfo.CurrentTool,
                        //bloomPUB = new { imageSettings = new { maxWidth= _bookSelection.CurrentSelection.BookInfo.PublishSettings.BloomPub.ImageSettings.MaxWidth, maxHeight= _bookSelection.CurrentSelection.BookInfo.PublishSettings.BloomPub.ImageSettings.MaxHeight} }
                        publish = _bookSelection.CurrentSelection.BookInfo.PublishSettings,
                        appearance = _bookSelection
                            .CurrentSelection
                            .BookInfo
                            .AppearanceSettings
                            .ChangeableSettingsForUI
                    };
                    var jsonData = JsonConvert.SerializeObject(settings);

                    request.ReplyWithJson(jsonData);
                    break;
                case HttpMethods.Post:
                    var json = request.RequiredPostJson();
                    dynamic newSettings = Newtonsoft.Json.Linq.JObject.Parse(json);
                    //var c = newSettings.appearance.cover.coverColor;
                    //_bookSelection.CurrentSelection.SetCoverColor(c.ToString());
                    // review: crazy bit here, that above I'm taking json, parsing it into and object, and grabbing part of it. But then
                    // here we take it back to json and pass it to this thing that is going to parse it again. In this case, speed
                    // is irrelevant. The nice thing is, it retains the identity of PublishSettings in case someone is holding onto it.
                    var jsonOfJustPublishSettings = JsonConvert.SerializeObject(
                        newSettings.publish
                    );
                    _bookSelection.CurrentSelection.BookInfo.PublishSettings.LoadNewJson(
                        jsonOfJustPublishSettings
                    );
                    _bookSelection.CurrentSelection.BookInfo.AppearanceSettings.UpdateFromDynamic(
                        newSettings.appearance
                    );

                    _bookSelection.CurrentSelection.SettingsUpdated();

                    // we want a "full" save, which means that the <links> in the <head> can be regenerated, i.e. in response
                    // to a change in the CssTheme from/to legacy that requires changing between "basePage.css" and "basePage-legacy-5-6.css"
                    _pageRefreshEvent.Raise(
                        PageRefreshEvent.SaveBehavior.SaveBeforeRefreshFullSave
                    );
                    IndicatorInfoApi.NotifyIndicatorInfoChanged();

                    request.PostSucceeded();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private bool GetIsBookATemplate()
        {
            return _bookSelection.CurrentSelection.IsSuitableForMakingShells;
        }
    }
}
