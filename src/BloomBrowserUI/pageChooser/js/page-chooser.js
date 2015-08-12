/// <reference path="../../bookEdit/js/getIframeChannel.ts" />
window.addEventListener('message', process_EditFrame_Message, false);
var _pageChooser;
function process_EditFrame_Message(event) {
    var params = event.data.split('\n');
    switch (params[0]) {
        case 'AddSelectedPage':
            if (_pageChooser)
                _pageChooser.addPageClickHandler();
            else
                alert("received AddSelectedPage message before PageChooser was created");
            return;
        case 'Data':
            _pageChooser = new PageChooser(params[1]);
            _pageChooser.LoadInstalledCollections();
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
            console.log('Expected url in PageChooser ctor!');
        }
        this._selectedTemplatePage = undefined;
    }
    PageChooser.prototype.thumbnailClickHandler = function (div) {
        // 'div' is an .invisibleThumbCover
        // Mark any previously selected thumbnail as no longer selected
        if (this._selectedTemplatePage != undefined) {
            $(this._selectedTemplatePage).removeClass('ui-selected');
        }
        // Select new thumbnail
        this._selectedTemplatePage = $(div).parent();
        $(this._selectedTemplatePage).addClass('ui-selected');
        // Display large preview
        var caption = $('#previewCaption');
        caption.text($('.templatePageCaption', this._selectedTemplatePage).text());
        caption.attr('style', 'display: block;');
        $('#preview').attr('src', $(this._selectedTemplatePage).find('iframe').first().attr('src'));
        // TODO: disable the main dialog one! $( '#addPageButton' ).prop( "disabled", false );
    }; // thumbnailClickHandler
    PageChooser.prototype.addPageClickHandler = function () {
        if (this._selectedTemplatePage == undefined || this._templateBookUrls == undefined) {
            return null; // TODO: say something to the user!?
        }
        var pageId = $(this._selectedTemplatePage).find('iframe').first().attr('src');
        console.log('firing CSharp event - Selected template page: ' + pageId);
        this.fireCSharpEvent('addPage', pageId);
    }; // addPageClickHandler
    PageChooser.prototype.LoadInstalledCollections = function () {
        // Originally (now maybe YAGNI) the dialog handled more than one collection of template pages.
        // Right now it only handles one, so the cloning of stub html is perhaps unnecessary,
        // but I've left it in case we need it later.
        // Save html sections that will get cloned later
        // there should only be one 'collection' at this point; a stub with one default template page
        var collectionHTML = $('.collection', document).first().clone();
        // there should only be the one default 'templatePage' at this point
        var templatePageHTML = $('.templatePage', collectionHTML).first().clone();
        var collectionUrls;
        try {
            collectionUrls = $.parseJSON(_pageChooser._templateBookUrls);
        }
        catch (e) {
            console.log('Received bad template url: ' + e);
        }
        if ($(collectionUrls).length > 0) {
            // Remove original stub section
            $('.outerCollectionContainer', document).empty();
            $.each(collectionUrls, function (index) {
                //console.log('  ' + (index + 1) + ' loading... ' + this['templateBookUrl'] );
                _pageChooser.LoadCollection(this['templateBookUrl'], collectionHTML, templatePageHTML);
            });
            window.scrollTo(0, 0); // TODO: wrong window!
        }
    }; // LoadInstalledCollections
    PageChooser.prototype.LoadCollection = function (pageUrl, collectionHTML, templatePageHTML) {
        var request = $.get(pageUrl);
        request.done(function (pageData) {
            // TODO: for now just grab the first book title, we may want to know which lang to grab eventually
            var dataBookArray = $("div[data-book='bookTitle']", pageData);
            var collectionTitle = $(dataBookArray.first()).text();
            // Add title and container to dialog
            var collectionToAdd = $(collectionHTML).clone();
            $(collectionToAdd).find('.collectionCaption').text(collectionTitle);
            $('.outerCollectionContainer', document).append(collectionToAdd);
            // Grab all pages in this collection
            // N.B. normal selector syntax or .find() WON'T work here because pageData is not yet part of the DOM!
            var pages = $(pageData).filter(".bloom-page[id]");
            _pageChooser.LoadPagesFromCollection(collectionToAdd, pages, templatePageHTML, pageUrl);
        }, "html");
        request.fail(function (jqXHR, textStatus, errorThrown) {
            console.log('There was a problem reading: ' + pageUrl + ' see documentation on : ' + jqXHR.status + ' ' + textStatus + ' ' + errorThrown);
        });
    }; // LoadCollection
    PageChooser.prototype.LoadPagesFromCollection = function (currentCollection, pageArray, templatePageHTML, url) {
        if ($(pageArray).length < 1) {
            return;
        }
        // Remove default template page
        $('.innerCollectionContainer', currentCollection).empty();
        // insert a template page for each page with the correct #id on the url in the iframe
        $(pageArray).each(function (index, div) {
            var currentId = $(div).attr('id');
            // TODO: for now just grab the first page label, we may want to know which lang to grab eventually
            var pageTitle = $('.pageLabel', div).first().text();
            var currentPageHTML = $(templatePageHTML).clone();
            $('.templatePageCaption', currentPageHTML).first().text(pageTitle);
            $('iframe', currentPageHTML).attr('src', url + '#' + currentId);
            $('.innerCollectionContainer', currentCollection).append(currentPageHTML);
        }); // each
        // once the template pages are installed, attach click handler to them.
        $('.invisibleThumbCover', currentCollection).each(function (index, div) {
            $(div).click(function () {
                _pageChooser.thumbnailClickHandler(div);
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