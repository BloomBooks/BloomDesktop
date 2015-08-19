/// <reference path="../../bookEdit/js/getIframeChannel.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
window.addEventListener("message", process_EditFrame_Message, false);
function process_EditFrame_Message(event) {
    var params = event.data.split("\n");
    switch (params[0]) {
        case "Data":
            var pageChooser = new PageChooser(params[1]);
            pageChooser.loadInstalledCollections();
            return;
        default:
    }
}
// this version of the test string may be useful later testing more than one template collection
//var JSONTestString = "[{ \"templateBookUrl\": \"../../../DistFiles/factoryCollections/Templates/Basic Book/Basic Book.htm\" }, { \"templateBookUrl\": \"../../../DistFiles/factoryCollections/Templates/Basic Book/Basic Book.htm\" }]";
// no longer using test string, but let's keep it around as documentation of what PageChooser's ctor is expecting
//var JSONTestString = "[{ \"templateBookUrl\": \"bloom/localhost/C$/BloomDesktop/DistFiles/factoryCollections/Templates/Basic Book/Basic Book.htm\" }]";
var PageChooser = (function () {
    function PageChooser(templateBookUrls) {
        if (templateBookUrls) {
            this._templateBookUrls = templateBookUrls;
        }
        else {
            console.log("Expected url in PageChooser ctor!");
        }
        this._selectedGridItem = undefined;
    }
    PageChooser.prototype.thumbnailClickHandler = function (clickedDiv) {
        // 'div' is an .invisibleThumbCover
        // Mark any previously selected thumbnail as no longer selected
        if (this._selectedGridItem != undefined) {
            $(this._selectedGridItem).removeClass("ui-selected");
        }
        // Select new thumbnail
        this._selectedGridItem = $(clickedDiv).parent();
        $(this._selectedGridItem).addClass("ui-selected");
        // Display large preview
        var caption = $('#previewCaption');
        var defaultCaptionText = $(".gridItemCaption", this._selectedGridItem).text();
        this.setLocalizedText(caption, 'TemplateBooks.PageLabel.', defaultCaptionText);
        caption.attr("style", "display: block;");
        $("#preview").attr("src", $(this._selectedGridItem).find("img").first().attr("src"));
        this.setLocalizedText($('#previewDescriptionText'), 'TemplateBooks.PageDescription.', $(".pageDescription", this._selectedGridItem).text(), defaultCaptionText);
    }; // thumbnailClickHandler
    // Set the text of the given element to the appropriate localization of defaultText
    // (or to defaultText, if no localization is available).
    // If defaultText is empty, set the element text to empty.
    // The localization ID to look up is made by concatenating the supplied prefix and the id
    // parameter, which defaults to the defaultText since we often use the English text of a
    // label as the last part of its ID.
    PageChooser.prototype.setLocalizedText = function (elt, idPrefix, defaultText, id) {
        if (id === void 0) { id = defaultText; }
        if (defaultText) {
            localizationManager.asyncGetText(idPrefix + id, defaultText)
                .done(function (translation) {
                elt.text(translation);
            });
        }
        else {
            elt.text("");
        }
    };
    PageChooser.prototype.addPageClickHandler = function () {
        if (this._selectedGridItem == undefined || this._templateBookUrls == undefined) {
            return null;
        }
        this.fireCSharpEvent("setModalStateEvent", "false");
        var id = this._selectedGridItem.attr("data-pageId");
        this.fireCSharpEvent("addPage", id);
    }; // addPageClickHandler
    PageChooser.prototype.loadInstalledCollections = function () {
        // Originally (now maybe YAGNI) the dialog handled more than one collection of template pages.
        // Right now it only handles one, so the cloning of stub html is perhaps unnecessary,
        // but I've left it in case we need it later.
        var _this = this;
        // Save html sections that will get cloned later
        // there should only be one 'collection' at this point; a stub with one default template page
        var collectionHtml = $(".collection", document).first().clone();
        // there should only be the one default 'gridItem' at this point
        var gridItemHtml = $(".gridItem", collectionHtml).first().clone();
        var collectionUrls;
        try {
            collectionUrls = $.parseJSON(this._templateBookUrls);
        }
        catch (e) {
            console.log("Received bad template url: " + e);
            return;
        }
        var pageChooser = this;
        if ($(collectionUrls).length > 0) {
            // Remove original stub section
            $(".outerCollectionContainer", document).empty();
            $.each(collectionUrls, function (index) {
                //console.log('  ' + (index + 1) + ' loading... ' + this['templateBookUrl'] );
                pageChooser.loadCollection(this["templateBookFolderUrl"], this["templateBookUrl"], collectionHtml, gridItemHtml);
            });
        }
        $("#addPageButton", document).button().click(function () {
            _this.fireCSharpEvent("setModalStateEvent", "false");
            _this.addPageClickHandler();
        });
        var pageButton = $("#addPageButton", document);
        localizationManager.asyncGetText('AddPageDialog.AddPageButton', 'Add This Page')
            .done(function (translation) {
            pageButton.attr('value', translation);
        });
        //TODO: choose which one to select based on some other criteria than just being first
        window.setTimeout(function () {
            _this.thumbnailClickHandler($(".invisibleThumbCover").first());
            //(<any>$).notify("Hint: Double-clicking a thumbnail adds it immediately", { className:"subtleHint",globalPosition:"bottom left",autoHide:false});
        }, 100);
    }; // LoadInstalledCollections
    PageChooser.prototype.loadCollection = function (pageFolderUrl, pageUrl, collectionHTML, gridItemHTML) {
        var _this = this;
        var request = $.get(pageUrl);
        request.done(function (pageData) {
            var dataBookArray = $("div[data-book='bookTitle']", pageData);
            var collectionTitle = $(dataBookArray.first()).text();
            // Add title and container to dialog
            var collectionToAdd = $(collectionHTML).clone();
            _this.setLocalizedText($(collectionToAdd).find(".collectionCaption"), 'TemplateBooks.BookName.', collectionTitle);
            $(".outerCollectionContainer", document).append(collectionToAdd);
            // Grab all pages in this collection
            // N.B. normal selector syntax or .find() WON'T work here because pageData is not yet part of the DOM!
            var pages = $(pageData).filter(".bloom-page[id]");
            _this.loadPagesFromCollection(collectionToAdd, pages, gridItemHTML, pageFolderUrl, pageUrl);
        }, "html");
        request.fail(function (jqXHR, textStatus, errorThrown) {
            console.log("There was a problem reading: " + pageUrl + " see documentation on : " +
                jqXHR.status + " " + textStatus + " " + errorThrown);
        });
    }; // LoadCollection
    PageChooser.prototype.loadPagesFromCollection = function (currentCollection, pageArray, gridItemTemplate, pageFolderUrl, pageUrl) {
        var _this = this;
        if ($(pageArray).length < 1) {
            return;
        }
        // Remove default template page
        $(".innerCollectionContainer", currentCollection).empty();
        // insert a template page for each page with the correct #id on the url
        $(pageArray).each(function (index, div) {
            if ($(div).attr("data-page") === "singleton")
                return; // skip this one
            var currentGridItemHtml = $(gridItemTemplate).clone();
            var currentId = $(div).attr("id");
            $(currentGridItemHtml).attr("data-pageId", currentId);
            var pageDescription = $(".pageDescription", div).first().text();
            $(".pageDescription", currentGridItemHtml).first().text(pageDescription);
            var pageLabel = $(".pageLabel", div).first().text();
            $(".gridItemCaption", currentGridItemHtml).first().text(pageLabel);
            pageLabel = pageLabel.replace("&", "+"); //ampersands don't work in the svg file names, so we use "+" instead
            $("img", currentGridItemHtml).attr("src", pageFolderUrl + "/template" + "/" + pageLabel + ".svg");
            $(".innerCollectionContainer", currentCollection).append(currentGridItemHtml);
        }); // each
        // once the template pages are installed, attach click handler to them.
        $(".invisibleThumbCover", currentCollection).each(function (index, div) {
            $(div).dblclick(function () {
                _this.addPageClickHandler();
            }); // invisibleThumbCover double click
            $(div).click(function () {
                _this.thumbnailClickHandler(div);
            }); // invisibleThumbCover click
        }); // each
    }; // LoadPagesFromCollection
    /**
     * Fires an event for C# to handle
     * @param {String} eventName
     * @param {String} eventData
     */
    PageChooser.prototype.fireCSharpEvent = function (eventName, eventData) {
        //console.log('firing CSharp event: ' + eventName);
        var event = new MessageEvent(eventName, { 'view': window, 'bubbles': true, 'cancelable': true, 'data': eventData });
        document.dispatchEvent(event);
    };
    return PageChooser;
})();
//# sourceMappingURL=page-chooser.js.map