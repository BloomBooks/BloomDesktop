using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Book;
using Bloom.Edit;
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
        private EditingView _editingView;

        public BookSettingsApi(
            BookSelection bookSelection,
            PageRefreshEvent pageRefreshEvent,
            BookRefreshEvent bookRefreshEvent,
            EditingView editingView
        )
        {
            _bookSelection = bookSelection;
            _pageRefreshEvent = pageRefreshEvent;
            _bookRefreshEvent = bookRefreshEvent;
            _editingView = editingView;
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
                "book/settings/overrides",
                HandleGetOverrides,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "book/settings/deleteCustomBookStyles",
                HandleDeleteCustomBookStyles,
                false
            );
        }

        private void HandleGetOverrides(ApiRequest request)
        {
            var x = new ExpandoObject() as IDictionary<string, object>;
            // The values set here should correspond to the declaration of IOverrideValues
            // in BookSettingsDialog.tsx.
            x["branding"] = _bookSelection.CurrentSelection.Storage.BrandingAppearanceSettings;
            x["xmatter"] = _bookSelection.CurrentSelection.Storage.XmatterAppearanceSettings;
            x["brandingName"] = _bookSelection
                .CurrentSelection
                .CollectionSettings
                .BrandingProjectKey;
            x["xmatterName"] = _bookSelection.CurrentSelection.CollectionSettings.XMatterPackName;
            request.ReplyWithJson(JsonConvert.SerializeObject(x));
        }

        private void HandleDeleteCustomBookStyles(ApiRequest request)
        {
            // filename is usually "customBookStyles.css" but could possibly be "customCollectionStyles.css"
            var filename = request.GetParamOrNull("file");
            if (filename == null)
            {
                request.Failed("No file specified");
                return;
            }
            RobustFile.Delete(Path.Combine(_bookSelection.CurrentSelection.FolderPath, filename));
            _bookSelection.CurrentSelection.SettingsUpdated();
            // We should only delete it when it's not in use, so we should not need to refresh the page.
            IndicatorInfoApi.NotifyIndicatorInfoChanged();
            request.PostSucceeded();
        }

        private void HandleGetAvailableAppearanceUIOptions(ApiRequest request)
        {
            request.ReplyWithJson(
                _bookSelection.CurrentSelection.BookInfo.AppearanceSettings.AppearanceUIOptions(
                    _bookSelection.CurrentSelection.Storage.LegacyThemeCanBeUsed
                )
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
                    // The book settings dialog wants to edit the content language visibility as if it was just another
                    // appearance setting. But we have another control that manipulates it, and a long-standing place to
                    // store it that is NOT in appearance.json. So for this purpose we pretend it is a set of three
                    // appearance settings that follow the pattern for controlling which languages are shown for a field.
                    var appearance = (settings.appearance as IDictionary<string, object>);
                    //var collectionLangs = _bookSelection.CurrentSelection.CollectionSettings.LanguagesZeroBased;
                    var bookLangs = new HashSet<string>();
                    if (_bookSelection.CurrentSelection.Language1Tag != null)
                    {
                        bookLangs.Add(_bookSelection.CurrentSelection.Language1Tag);
                    }

                    if (_bookSelection.CurrentSelection.Language2Tag != null)
                    {
                        bookLangs.Add(_bookSelection.CurrentSelection.Language2Tag);
                    }
                    if (_bookSelection.CurrentSelection.Language3Tag != null)
                    {
                        bookLangs.Add(_bookSelection.CurrentSelection.Language3Tag);
                    }

                    appearance["mlcontent-L1-show"] = bookLangs.Contains(
                        _bookSelection.CurrentSelection.CollectionSettings.Language1Tag
                    );
                    appearance["mlcontent-L2-show"] = bookLangs.Contains(
                        _bookSelection.CurrentSelection.CollectionSettings.Language2Tag
                    );
                    appearance["mlcontent-L3-show"] = bookLangs.Contains(
                        _bookSelection.CurrentSelection.CollectionSettings.Language3Tag
                    );
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
                    // Now we need to extract the content language visibility settings and remove them from what gets saved
                    // as the appearance settings.
                    var newAppearance = newSettings.appearance;
                    var showL1 = newAppearance["mlcontent-L1-show"].Value;
                    newAppearance.Remove("mlcontent-L1-show");
                    var showL2 = newAppearance["mlcontent-L2-show"].Value;
                    newAppearance.Remove("mlcontent-L2-show");
                    var showL3 = newAppearance["mlcontent-L3-show"].Value;
                    newAppearance.Remove("mlcontent-L3-show");
                    _editingView.SetActiveLanguages(showL1, showL2, showL3);
                    // Todo: save the content languages
                    _bookSelection.CurrentSelection.BookInfo.AppearanceSettings.UpdateFromDynamic(
                        newAppearance
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
