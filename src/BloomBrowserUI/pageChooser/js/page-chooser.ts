/// <reference path="../../bookEdit/js/getIframeChannel.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />

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
        } else {
            console.log("Expected url in PageChooser ctor!");
        }

        this._selectedGridItem = undefined;
        this._indexOfPageToSelect = 0;
    }

    thumbnailClickHandler( clickedDiv ) : void {
        // 'div' is an .invisibleThumbCover
        // Mark any previously selected thumbnail as no longer selected
        if (this._selectedGridItem != undefined) {
            $(this._selectedGridItem).removeClass("ui-selected");
        }
        // Select new thumbnail
        this._selectedGridItem = $( clickedDiv ).parent();
        $(this._selectedGridItem).addClass("ui-selected");

        // Display large preview
        var caption = $('#previewCaption');
        var defaultCaptionText = $(".gridItemCaption", this._selectedGridItem).text();
        this.setLocalizedText(caption, 'TemplateBooks.PageLabel.', defaultCaptionText);
        caption.attr("style", "display: block;");
        $("#preview").attr("src", $(this._selectedGridItem).find("img").first().attr("src"));
        this.setLocalizedText($('#previewDescriptionText'), 'TemplateBooks.PageDescription.', $(".pageDescription", this._selectedGridItem).text(), defaultCaptionText);
    } // thumbnailClickHandler

    // Set the text of the given element to the appropriate localization of defaultText
    // (or to defaultText, if no localization is available).
    // If defaultText is empty, set the element text to empty.
    // The localization ID to look up is made by concatenating the supplied prefix and the id
    // parameter, which defaults to the defaultText since we often use the English text of a
    // label as the last part of its ID.
    setLocalizedText(elt: JQuery, idPrefix: string, defaultText: string, id: string = defaultText) {
        if (defaultText) {
            localizationManager.asyncGetText(idPrefix + id, defaultText)
                .done(translation => {
                    elt.text(translation);
                });
        } else {
            elt.text("");
        }
    }

    addPageClickHandler() : void {
        if (this._selectedGridItem == undefined || this._templateBookUrls == undefined) return;
        this.fireCSharpEvent("setModalStateEvent", "false");
        var id = this._selectedGridItem.attr("data-pageId"); 
        this.fireCSharpEvent("addPage", id);
    } // addPageClickHandler

    loadInstalledCollections(): void {

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
        var pageButton = $("#addPageButton", document);
        localizationManager.asyncGetText('AddPageDialog.AddPageButton', 'Add This Page')
            .done(translation => {
                pageButton.attr('value', translation);
            });
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
            var pages = $( pageData).filter( ".bloom-page[id]" );
            this._indexOfPageToSelect = this.loadPagesFromCollection(collectionToAdd, pages, gridItemHTML, pageFolderUrl, pageUrl, lastPageAdded);
            this.thumbnailClickHandler($(".invisibleThumbCover").eq(this._indexOfPageToSelect));
        }, "html");
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
            if (currentId === lastPageAdded)
                indexToSelect = index;
            
            var pageDescription = $(".pageDescription", div).first().text();
            $(".pageDescription", currentGridItemHtml).first().text(pageDescription);

            var pageLabel = $(".pageLabel", div).first().text();
            $(".gridItemCaption", currentGridItemHtml).first().text(pageLabel);

            $("img", currentGridItemHtml).attr("src", this.buildThumbSrcFilename(pageFolderUrl, pageLabel));
            $(".innerCollectionContainer", currentCollection).append(currentGridItemHtml);
        }); // each
        // once the template pages are installed, attach click handler to them.
        $(".invisibleThumbCover", currentCollection).each((index, div) => {
            $(div).dblclick(() => {
                this.addPageClickHandler();
            }); // invisibleThumbCover double click

            $(div).click(() => {
                this.thumbnailClickHandler(div);
            }); // invisibleThumbCover click
        }); // each
        return indexToSelect;
    } // LoadPagesFromCollection

    buildThumbSrcFilename(pageFolderUrl: string, pageLabel: string): string {
        var label = pageLabel.replace('&', '+'); //ampersands don't work in the svg file names, so we use "+" instead
        return pageFolderUrl + '/template/' + label + (this._orientation === 'landscape' ? '-landscape' : '') + '.svg';
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
