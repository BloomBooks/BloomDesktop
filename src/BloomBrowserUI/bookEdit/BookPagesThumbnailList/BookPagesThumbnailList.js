// jquery plugin for showing a list of thumbnails in an html file that has divs for each page
(function ($) {
        $.widget("bloom.pagesThumbnailList", {
            options: {
                //bookUrl: "../../../../DistFiles/factoryCollections/Templates/Basic Book/Basic Book.htm",
                bookUrl: "Vaccinations/Vaccinations.htm",
                selectedPage: undefined,
            },

            //Setup widget (eg. element creation, apply theming
            // , bind events etc.)
            _create: function () {
                // this.element -- a jQuery object of the element the widget was invoked on.
                // this.options --  the merged options hash
                //alert('create: ' + this.options.bookUrl);
                this._refresh(this.options.bookUrl);
            },

            _refresh: function () {
                //this.SelectedPage = undefined;
                this.element.empty();
                if (this.options.bookUrl === undefined) {
                    return;
                }

                (function (url, listTOAddThumbnailsTo) {
                    $.get(url, null, null, "html")
                        .done(
                            function(pageData) {
                                //                 // TODO: for now just grab the first book title, we may want to know which lang to grab eventually
                                //                 var dataBookArray = $("div[data-book='bookTitle']", pageData);
                                //                 var collectionTitle = $(dataBookArray.first()).text();
                                //                 // Add title and container to dialog
                                //                 var collectionToAdd = $(collectionHtml).clone();
                                //                 $(collectionToAdd).find('.collectionCaption').text(collectionTitle);
                                //                 $('.outerCollectionContainer', document).append(collectionToAdd);
                                // Grab all pageElements in this collection
                                // N.B. normal selector syntax or .find() WON'T work here because pageData is not yet part of the DOM!

                                // Remove default template page
                                //$('.innerCollectionContainer', currentCollection).empty();
                                // insert a template page for each page with the correct #id on the url in the iframe

                                var pageDivs = $(pageData).filter(".bloom-page[id]");

                                $(pageDivs).each(function(div) {
                                    var currentId = $(this).attr('id');
//                                // TODO: for now just grab the first page label, we may want to know which lang to grab eventually
//                                var pageTitle = $('.pageLabel', div).first().text();
                                    //$('.templatePageCaption', currenttemplateHtml).first().text(pageTitle);

                                    //$(listTOAddThumbnailsTo).append($('<li data-w="1" data-h="1"><iframe src="' + url + '#' + currentId + '"></iframe></li>'));
                                    $(listTOAddThumbnailsTo).append($('<li data-w="1" data-h="1">Hello</li>'));
                                });
                                // once the template pageElements are installed, attach click handler to them.
//                            $('.invisibleThumbCover', currentCollection).each(function (index, div) {
//                                $(div).click(function () {
//                                    ThumbnailClickHandler(div);
//                                }); // invisibleThumbCover click
//                            });
                            })
                        .fail(function(jqXHR, textStatus, errorThrown) {
                            console.log('There was a problem reading the url. ' + jqXHR.status + ' ' + textStatus + ' ' + errorThrown);
                        });
                })(this.options.bookUrl, this.element);
            },

            ThumbnailClickHandler: function (div) {
                // 'div' is an .invisibleThumbCover
                // Mark any previously selected thumbnail as no longer selected
                if (this.SelectedPage !== undefined) {
                    $(this.SelectedPage).removeClass('ui-selected');
                }
                // Select new thumbnail
                this.SelectedPage = $(div).parent();
                $(this.SelectedPage).addClass('ui-selected');
                // Display large preview
                $('#previewCaption').text($('.templatePageCaption', this.SelectedPage).text());
                $('#preview').attr('src', $(this.SelectedPage).find('iframe').first().attr('src'));
                $('#addPageButton').prop("disabled", false);
            },

            AddPageClickHandler: function () {
                if (this.SelectedPage === undefined || this.BookUrl === undefined) {
                    return;
                }
                // TODO: Add page to book here
                console.log('Selected template page: ' + $(this.SelectedPage).find('iframe').first().attr('src'));
                console.log('Input urls: ' + this.BookUrl);
                // Mark any previously selected thumbnail as no longer selected
                $(this.SelectedPage).removeClass('ui-selected');
            },

            // Respond to any changes the user makes to the
            // option method
            _setOption: function ( key, value ) {
                switch (key) {
                    case "url":
                        this.options.bookUrl = value;
                        break;
                    default:
                        //this.options[ key ] = value;
                        break;
                }

                // For UI 1.9 the _super method can be used instead
                this._super( "_setOption", key, value );
            },

            // Destroy an instantiated plugin and clean up
            // modifications the widget has made to the DOM
            _destroy: function () {
                // For UI 1.9, define _destroy instead and don't
                // worry about
                // calling the base widget
            },
        });
    })(jQuery);