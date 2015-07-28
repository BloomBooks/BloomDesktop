/// <reference path="../../lib/jquery.d.ts" />
// this version of the test string may be useful later testing more than one template collection
//var JSONTestString = "[{ \"templateBookUrl\": \"../../../DistFiles/factoryCollections/Templates/Basic Book/Basic Book.htm\" }, { \"templateBookUrl\": \"../../../DistFiles/factoryCollections/Templates/Basic Book/Basic Book.htm\" }]";
var JSONTestString = "[{ \"templateBookUrl\": \"../../../DistFiles/factoryCollections/Templates/Basic Book/Basic Book.htm\" }]";
var PageChooser = (function () {
    // Constructor
    function PageChooser(templateBookUrls) {
        this._templateBookUrls = templateBookUrls;
        this._selectedTemplatePage = undefined;
        if (this._templateBookUrls != undefined) {
            LoadInstalledCollections();
        }
    }
    $(document).ready(function () {
        var addPage = $("#addPageButton");
        addPage.prop("disabled", true);
        addPage.click(function () {
            addPageClickHandler();
        }); // Add Page button click
        // Comment out this function to disable testButton; or better yet just hide it in the css
        $("#testButton").click(function () {
            testButtonClickHandler();
        }); // Test button click
    }); // document ready
    var thumbnailClickHandler = function (div) {
        // 'div' is an .invisibleThumbCover
        // Mark any previously selected thumbnail as no longer selected
        if (this._selectedTemplatePage != undefined) {
            $(this._selectedTemplatePage).removeClass('ui-selected');
        }
        // Select new thumbnail
        this._selectedTemplatePage = $(div).parent();
        $(this._selectedTemplatePage).addClass('ui-selected');
        // Display large preview
        $('#previewCaption').text($('.templatePageCaption', this._selectedTemplatePage).text());
        $('#previewCaption').attr('style', 'display: block;');
        $('#preview').attr('src', $(this._selectedTemplatePage).find('iframe').first().attr('src'));
        $('#addPageButton').prop("disabled", false);
    }; // thumbnailClickHandler
    var addPageClickHandler = function () {
        if (this._selectedTemplatePage == undefined || this._templateBookUrls == undefined) {
            return null;
        }
        // TODO: Add page to book here
        var pageId = $(this._selectedTemplatePage).find('iframe').first().attr('src');
        console.log('Selected template page: ' + pageId);
        console.log('Input urls: ' + this._templateBookUrls);
        // Mark any previously selected thumbnail as no longer selected
        $(this._selectedTemplatePage).removeClass('ui-selected');
    }; // AddPageClickHandler
    var cancelButtonClickHandler = function () {
        $(this).dialog("close");
    };
    var LoadInstalledCollections = function () {
        // Save html sections that will get cloned later
        // there should only be one 'collection' at this point; a stub with one default template page
        var collectionHTML = $('.collection', document).first().clone();
        // there should only be the one default 'templatePage' at this point
        var pageHTML = $('.templatePage', collectionHTML).first().clone();
        console.log('Loading installed template pages...');
        var collectionUrls = $.parseJSON(this._templateBookUrls);
        if ($(collectionUrls).length > 0) {
            // Remove original stub section
            $('.outerCollectionContainer', document).empty();
            $.each(collectionUrls, function (index) {
                console.log('  ' + (index + 1) + ' loading... ' + this['templateBookUrl']);
                LoadCollection(this['templateBookUrl'], collectionHTML, pageHTML);
            });
            window.scrollTo(0, 0); // TODO: wrong window!
            console.log('Available pages loaded.');
        }
    }; // LoadInstalledCollections
    var LoadCollection = function (pageUrl, collectionHTML, pageHTML) {
        var request = $.get(pageUrl);
        request.success(function (pageData) {
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
            LoadPagesFromCollection(collectionToAdd, pages, pageHTML, pageUrl);
        }, "html");
        request.error(function (jqXHR, textStatus, errorThrown) {
            console.log('There was a problem reading: ' + pageUrl + ' see documentation on : ' + jqXHR.status + ' ' + textStatus + ' ' + errorThrown);
        });
    }; // LoadCollection
    var LoadPagesFromCollection = function (currentCollection, pageArray, pageHTML, url) {
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
            var currentPageHTML = $(pageHTML).clone();
            $('.templatePageCaption', currentPageHTML).first().text(pageTitle);
            $('iframe', currentPageHTML).attr('src', url + '#' + currentId);
            $('.innerCollectionContainer', currentCollection).append(currentPageHTML);
        }); // each
        // once the template pages are installed, attach click handler to them.
        $('.invisibleThumbCover', currentCollection).each(function (index, div) {
            $(div).click(function () {
                thumbnailClickHandler(div);
            }); // invisibleThumbCover click
        }); // each
    }; // LoadPagesFromCollection
    var testButtonClickHandler = function () {
        this._templateBookUrls = JSONTestString;
        LoadInstalledCollections();
    }; // testButtonClickHandler
    return PageChooser;
})();
//# sourceMappingURL=page-chooser.js.map