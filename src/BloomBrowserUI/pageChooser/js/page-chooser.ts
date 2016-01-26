/// <reference path="../../bookEdit/js/getIframeChannel.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
import * as $ from 'jquery';
import * as jQuery from 'jquery';
import theOneLocalizationManager from '../../lib/localizationManager/localizationManager';
import getIframeChannel from '../../bookEdit/js/getIframeChannel';
//import '../../modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js';
import 'jquery-ui/jquery-ui-1.10.3.custom.min.js';
        
        
window.addEventListener("message", process_EditFrame_Message, false);

function process_EditFrame_Message(event: MessageEvent): void {

    var params = event.data.split("\n");

    switch(params[0]) {
        case "Data":
            var pageChooser = new PageChooser(params[1]);
            pageChooser.loadInstalledCollections();
            return;

        default:
    }
}

// latest version of the expected JSON initialization string (from EditingModel.GetTemplateBookInfo)
// "{\"lastPageAdded\":\"(guid of template page)\",
//   \"orientation\":\"landscape\",
//   \"collections\":[{\"templateBookFolderUrl\":\"/bloom/localhost/C$/BloomDesktop/DistFiles/factoryCollections/Templates/Basic Book\",
//                     \"templateBookUrl\":\"/bloom/localhost/C$/BloomDesktop/DistFiles/factoryCollections/Templates/Basic Book/Basic Book.htm\"}]}"

class PageChooser {

    private _templateBookUrls: string;
    private _lastPageAdded: string;
    private _orientation: string;
    private _selectedGridItem: JQuery;
    private _indexOfPageToSelect: number;
    private _scrollingDiv: JQuery;
    private _scrollTopOfTheScrollingDiv: number;
    private _forChooseLayout: boolean;
    private _currentPageLayout: string;

    constructor(initializationJsonString: string) {
        var initializationObject;
        if (initializationJsonString) {
            try {
                initializationObject = $.parseJSON(initializationJsonString);
            } catch (e) {
                console.log("Received bad JSON string: " + e);
                return;
            }
            this._templateBookUrls = initializationObject["collections"];
            this._lastPageAdded = initializationObject["lastPageAdded"];
            this._orientation = initializationObject["orientation"];
            this._forChooseLayout = initializationObject["chooseLayout"];
            this._currentPageLayout = initializationObject['currentLayout'];
        } else {
            console.log("Expected url in PageChooser ctor!");
        }

        this._selectedGridItem = undefined;
        this._indexOfPageToSelect = 0;
        this._scrollTopOfTheScrollingDiv = 0;
    }

    thumbnailClickHandler( clickedDiv, evt ) : void {
        // 'div' is an .invisibleThumbCover
        // Select new thumbnail
        var newsel = this.findProperElement(clickedDiv, evt);
        if (newsel == null)
            return;
        // Mark any previously selected thumbnail as no longer selected
        if (this._selectedGridItem != undefined) {
            $(this._selectedGridItem).removeClass("ui-selected");
        }
        this._selectedGridItem = newsel;
        $(this._selectedGridItem).addClass("ui-selected");

        // Display large preview
        var caption = $('#previewCaption');
        var defaultCaptionText = $(".gridItemCaption", this._selectedGridItem).text();
        this.setLocalizedText(caption, 'TemplateBooks.PageLabel.', defaultCaptionText);
        caption.attr("style", "display: block;");
        $("#preview").attr("src", $(this._selectedGridItem).find("img").first().attr("src"));
        this.setLocalizedText($('#previewDescriptionText'), 'TemplateBooks.PageDescription.', $(".pageDescription", this._selectedGridItem).text(), defaultCaptionText);
        if (this._forChooseLayout) {
            var willLoseData = this.willLoseData();
            if (willLoseData) {
                $('#mainContainer').addClass("willLoseData");
            } else {
                $('#mainContainer').removeClass("willLoseData");
            }
            $('#convertAnywayCheckbox').prop('checked', !willLoseData);
            this.continueCheckBoxChanged(); // possibly redundant
        }
    } // thumbnailClickHandler

    // Return true if choosing the current layout will cause loss of data
    willLoseData(): boolean {
        var selected = $(this._selectedGridItem);
        var selectedEditableDivs = parseInt(selected.attr('data-textDivCount'));
        var selectedPictures = parseInt(selected.attr('data-picureCount'));

        var current = $((<HTMLIFrameElement>window.parent.document.getElementById('page')).contentWindow.document);
        var currentEditableDivs = current.find(".bloom-translationGroup").length;
        var currentPictures = current.find(".bloom-imageContainer").length;

        return selectedEditableDivs < currentEditableDivs || selectedPictures < currentPictures;
    }


    // There's a bug deep in javascript that doesn't take into account the scrolling
    // of a div element before something inside it is clicked on.  The following code
    // detects whether the scrolling has changed since the last mouse click, and if so,
    // searches for the item which should have matched.  For the initial bug report,
    // see https://silbloom.myjetbrains.com/youtrack/issue/BL-2623.
    // Note that the offset().top values returned by jquery properly take into account
    // the scrollTop of the scrolling parent div.  Which makes me think the bug may be
    // below the jquery level!?
    findProperElement(clickedDiv, evt): JQuery {
        var gridItem = $(clickedDiv).parent();
        if (evt) {
            var currentScrollTop = this._scrollingDiv.scrollTop();
            if (currentScrollTop !== this._scrollTopOfTheScrollingDiv) {
                // The scrolling position has changed, so we need to explicitly search
                // for the proper object.
                var y = evt["clientY"];     // retrieve the original click position
                var x = evt["clientX"];
                var container = $(clickedDiv).parent().parent();
                var childs = $(container).children();
                for (var i = 0; i < childs.length; ++i) {
                    var child = childs.eq(i);
                    var top = child.offset().top;
                    var bottom = top + child.height();
                    var left = child.offset().left;
                    var right = left + child.width();
                    if (top <= y && y <= bottom && left <= x && x <= right) {
                        // Remember the new scroll position and return the proper object.
                        this._scrollTopOfTheScrollingDiv = currentScrollTop;
                        return child;
                    }
                }
                // We couldn't find the proper object, so don't do anything.  The user
                // apparently clicked on a visually empty spot that got misidentified.
                return null;
            }
        }
        return gridItem;
    }


    // Set the text of the given element to the appropriate localization of defaultText
    // (or to defaultText, if no localization is available).
    // If defaultText is empty, set the element text to empty.
    // The localization ID to look up is made by concatenating the supplied prefix and the id
    // parameter, which defaults to the defaultText since we often use the English text of a
    // label as the last part of its ID.
    setLocalizedText(elt: JQuery, idPrefix: string, defaultText: string, id: string = defaultText) {
        if (defaultText) {
            theOneLocalizationManager.asyncGetText(idPrefix + id, defaultText)
                .done(translation => {
                    elt.text(translation);
                });
        } else {
            elt.text("");
        }
    }

    addPageClickHandler() : void {
        if (this._selectedGridItem == undefined || this._templateBookUrls == undefined) return;
        if (this._forChooseLayout && !$('#convertAnywayCheckbox').is(':checked')) return;
        this.fireCSharpEvent("setModalStateEvent", "false");
        var id = this._selectedGridItem.attr("data-pageId");
        if (this._forChooseLayout) {
            this.fireCSharpEvent("chooseLayout", id);

        } else {
            this.fireCSharpEvent("addPage", id);
        }
    } // addPageClickHandler

    continueCheckBoxChanged(): void {
        if (!this._forChooseLayout) return;
        var cb = $('#convertAnywayCheckbox');
        var isCurrentSelectionOriginal = this._selectedGridItem.hasClass('disabled');
        $('#addPageButton').prop('disabled', isCurrentSelectionOriginal || !cb.is(':checked'));
    }

    // This is the starting-point method that is invoked to initialize the dialog.
    // At the point where it is called, the json parameters that control what will be displayed
    loadInstalledCollections(): void {

        // Save a reference to the scrolling div that contains the various page items.
        this._scrollingDiv = $(".gridItemDisplay", document);

        // Originally (now maybe YAGNI) the dialog handled more than one collection of template pages.
        // Right now it only handles one, so the cloning of stub html is perhaps unnecessary,
        // but I've left it in case we need it later.

        // Save html sections that will get cloned later
        // there should only be one 'collection' at this point; a stub with one default template page
        var collectionHtml =  $(".collection", document).first().clone();
        // there should only be the one default 'gridItem' at this point
        var gridItemHtml = $( ".gridItem", collectionHtml).first().clone();
        if ($(this._templateBookUrls).length > 0) {
            // Remove original stub section
            $(".outerCollectionContainer", document).empty();
            $.each(this._templateBookUrls, (index, item) => {
                //console.log('  ' + (index + 1) + ' loading... ' + this['templateBookUrl'] );
                this.loadCollection(item["templateBookFolderUrl"], item["templateBookUrl"], collectionHtml, gridItemHtml, this._lastPageAdded);
            });
        }
        $("#addPageButton", document).button().click(() => {
            this.addPageClickHandler();
        });
        $("#convertAnywayCheckbox", document).button().change(() => {
            this.continueCheckBoxChanged();
        });
        var pageButton = $("#addPageButton", document);
        var okButtonLabelId = 'EditTab.AddPageDialog.AddThisPageButton';
        var okButtonLabelText = 'Add This Page';
        if (this._forChooseLayout) {
            okButtonLabelId = 'EditTab.AddPageDialog.ChooseLayoutButton';
            okButtonLabelText = 'Use This Layout';
            this.setLocalizedText($('#convertAnywayCheckbox'),'EditTab.AddPageDialog.', 'Continue anyway','ChooseLayoutContinueCheckbox')
            this.setLocalizedText($('#convertLosesMaterial'), 'EditTab.AddPageDialog.', 'Converting to this layout will cause some content to be lost.', 'ChooseLayoutWillLoseData')
       }
        theOneLocalizationManager.asyncGetText(okButtonLabelId, okButtonLabelText)
            .done(translation => {
                pageButton.attr('value', translation);
            });

        if (this._orientation === 'landscape') {
            $("#mainContainer").addClass("landscape");
        }
    } // LoadInstalledCollections

    loadCollection(pageFolderUrl, pageUrl, collectionHTML, gridItemHTML, lastPageAdded:string): void {
        var request = $.get(pageUrl);
        request.done( pageData => {
             var dataBookArray = $( "div[data-book='bookTitle']", pageData );
            var collectionTitle = $( dataBookArray.first() ).text();
            // Add title and container to dialog
            var collectionToAdd = $(collectionHTML).clone();
            this.setLocalizedText($(collectionToAdd).find(".collectionCaption"), 'TemplateBooks.BookName.', collectionTitle);
            $( ".outerCollectionContainer", document).append(collectionToAdd);
            // Grab all pages in this collection
            // N.B. normal selector syntax or .find() WON'T work here because pageData is not yet part of the DOM!
            var pages = $(pageData).filter('.bloom-page[id]').filter('[data-page="extra"]');
            if (this._forChooseLayout) {
               // This filters out the (empty) custom page, which is currently never a useful layout change, since all data would be lost.
               pages = pages.not('.bloom-page[id="5dcd48df-e9ab-4a07-afd4-6a24d0398386"]');
            }
            this._indexOfPageToSelect = this.loadPagesFromCollection(collectionToAdd, pages, gridItemHTML, pageFolderUrl, pageUrl, lastPageAdded);
            this.thumbnailClickHandler($(".invisibleThumbCover").eq(this._indexOfPageToSelect), null);
        });
        request.fail( function(jqXHR, textStatus, errorThrown) {
            console.log("There was a problem reading: " + pageUrl + " see documentation on : " +
                jqXHR.status + " " + textStatus + " " + errorThrown);
        });
    } // LoadCollection

    
    loadPagesFromCollection(currentCollection, pageArray, gridItemTemplate, pageFolderUrl, pageUrl, lastPageAdded:string ) : number {
        if ($(pageArray).length < 1) {
            return 0;
        }
        // Remove default template page
        $(".innerCollectionContainer", currentCollection).empty();

        var indexToSelect = 0;
        // insert a template page for each page with the correct #id on the url
        $(pageArray).each((index, div) => {

            if ($(div).attr("data-page") === "singleton")
                return;// skip this one

            var currentGridItemHtml = $(gridItemTemplate).clone();

            var currentId = $(div).attr("id");
            $(currentGridItemHtml).attr("data-pageId", currentId);
            $(currentGridItemHtml).attr("data-textDivCount", $(div).find(".bloom-translationGroup").length);
            $(currentGridItemHtml).attr("data-picureCount", $(div).find(".bloom-imageContainer").length);

            if (currentId === lastPageAdded)
                indexToSelect = index;
            if (currentId === this._currentPageLayout)
                $(currentGridItemHtml).addClass('disabled');
            
            var pageDescription = $(".pageDescription", div).first().text();
            $(".pageDescription", currentGridItemHtml).first().text(pageDescription);

            var pageLabel = $(".pageLabel", div).first().text().trim();
            $(".gridItemCaption", currentGridItemHtml).first().text(pageLabel);

            $("img", currentGridItemHtml).attr("src", this.buildThumbSrcFilename(pageFolderUrl, pageLabel));
            $(".innerCollectionContainer", currentCollection).append(currentGridItemHtml);
        }); // each
        // once the template pages are installed, attach click handler to them.
        $(".invisibleThumbCover", currentCollection).each((index, div) => {
            $(div).dblclick(() => {
                this.addPageClickHandler();
            }); // invisibleThumbCover double click

            $(div).click((evt) => {
                this.thumbnailClickHandler(div, evt);
            }); // invisibleThumbCover click
        }); // each
        return indexToSelect;
    } // LoadPagesFromCollection

    // any changes to how we tweak the page label to get a file name
    // must also be made in EnhancedImageServer.FindOrGenerateImage().
    buildThumbSrcFilename(pageFolderUrl: string, pageLabel: string): string {
        var label = pageLabel.replace('&', '+'); //ampersands don't work in the svg file names, so we use "+" instead
        // ?generateThumbnaiIfNecessary=true triggers logic in EnhancedImageServer.FindOrGenerateImage.
        // The result may actually be a png file or an svg, and there may be some delay while the png is generated.
        return pageFolderUrl + '/template/' + label + (this._orientation === 'landscape' ? '-landscape' : '') + '.svg?generateThumbnaiIfNecessary=true';
    }

    /**
     * Fires an event for C# to handle
     * @param {String} eventName
     * @param {String} eventData
     */
    fireCSharpEvent(eventName, eventData) : void {
        //console.log('firing CSharp event: ' + eventName);
        var event = new (<any>MessageEvent)(eventName, { 'view': window, 'bubbles': true, 'cancelable': true, 'data': eventData });
        document.dispatchEvent(event);
    }
}
