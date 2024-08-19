using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Edit;
using Bloom.web.controllers;
using Newtonsoft.Json;
using SIL.IO;
using System.Text.RegularExpressions;
using SIL.Extensions;
using Bloom.SafeXml;
using System.Windows;

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
                            .GetCopyOfProperties
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

                    appearance["autoTextBox-L1-show"] = bookLangs.Contains(
                        _bookSelection.CurrentSelection.CollectionSettings.Language1Tag
                    );
                    appearance["autoTextBox-L2-show"] = bookLangs.Contains(
                        _bookSelection.CurrentSelection.CollectionSettings.Language2Tag
                    );
                    appearance["autoTextBox-L3-show"] = bookLangs.Contains(
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
                    var showL1 = newAppearance["autoTextBox-L1-show"].Value;
                    newAppearance.Remove("autoTextBox-L1-show");
                    var showL2 = newAppearance["autoTextBox-L2-show"].Value;
                    newAppearance.Remove("autoTextBox-L2-show");
                    var showL3 = newAppearance["autoTextBox-L3-show"].Value;
                    newAppearance.Remove("autoTextBox-L3-show");
                    // Things get a little complex here. The three values we just computed indicate the desired visibility
                    // of the three collection languages. But L2 may well be the same as L1, and conceivably L3 might be the
                    // the same as L1 or L2 or both. If so, the controls that would be for duplicate languages are not shown,
                    // and their values are not updated. Worse, we are about to call SetActiveLanguages, and its arguments
                    // control the visibility of items in a de-duplicated list of languages.
                    // This seems as though it would have a more complicated effect than it actually does. We always show
                    // the control for L1, so showL1 is always valid. The third argument is only relevant if there are
                    // three distinct languages, so we can always pass showL3. The second argument is the tricky one:
                    // if L2 is the same as L1, then arg2 controls the visibility of L3, so we must pass showL3 (and ignore
                    // showL2, which is meaningless). If they are different, then showL2 controls the visibility of L2.
                    // (If all three are the same, the second and third arguments are irrelevant.
                    // If L3 is the same as L1 or L2, but L1 and L2 are different, then showL3 is meaningless, but also ignored,
                    // since there are only two languages and showL1 and showL2 are the only relevant arguments.)
                    var tag1 = _bookSelection.CurrentSelection.CollectionSettings.Language1Tag;
                    var tag2 = _bookSelection.CurrentSelection.CollectionSettings.Language2Tag;
                    _editingView.SetActiveLanguages(showL1, tag1 == tag2 ? showL3 : showL2, showL3);
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
