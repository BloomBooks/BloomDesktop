/** THIS FILE HAS BEEN CHANGED FROM THE ORIGINAL TO BETTER HANDLE COPYING FROM CHROME AND VS-CODE.
 * @license Copyright (c) 2003-2018, CKSource - Frederico Knabben. All rights reserved.
 * For licensing, see LICENSE.md or https://ckeditor.com/legal/ckeditor-oss-license
 */

(function() {
    "use strict";

    // Regex by Imme Emosol.
    var validUrlRegex = /^(https?|ftp):\/\/(-\.)?([^\s\/?\.#]+\.?)+(\/[^\s]*)?[^\s\.,]$/i,
        // Regex by (https://www.w3.org/TR/html5/forms.html#valid-e-mail-address).
        validEmailRegex = /^[a-zA-Z0-9.!#$%&'*+\/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/,
        doubleQuoteRegex = /"/g;

    CKEDITOR.plugins.add("autolink", {
        requires: "clipboard",

        init: function(editor) {
            editor.on("paste", function(evt) {
                var data = evt.data.dataValue;

                if (
                    evt.data.dataTransfer.getTransferType(editor) ==
                    CKEDITOR.DATA_TRANSFER_INTERNAL
                ) {
                    return;
                }

                // Chrome gratuitously wraps the copied data with "<!--StartFragment-->" and "<!--EndFragment-->"
                // when copying from the address bar.  Copying from inside VS-Code does the same on Windows.
                // On both Windows and Linux, VS-Code surrounds the copied text with "<p>" and "</p>" even
                // though it's not representing a paragraph.  The BloomField handler replaces a surrounding
                // "<p>" and "</p>" with a trailing "<p class='removeMe'></p>".  Depending on which paste handler
                // get called first, we may see either markup here.
                // On Linux, Chrome and VS-Code also terminate the data with "\u0000".
                // None of these should prevent the paste from automatically inserting a link.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-6845.
                if (
                    data.startsWith("<!--StartFragment-->") &&
                    data.endsWith("<!--EndFragment-->")
                ) {
                    data = data.substring(20);
                    data = data.substring(0, data.length - 18);
                }
                if (data.startsWith("<p>") && data.endsWith("</p>")) {
                    data = data.substring(3);
                    data = data.substring(0, data.length - 4);
                }
                if (data.endsWith("\u0000")) {
                    // I'm not sure this is needed, but it won't hurt anything.
                    data = data.substring(0, data.length - "\u0000".length);
                }
                if (data.endsWith("<p class='removeMe'></p>")) {
                    data = data.substring(0, data.length - 24);
                }
                // If we found "<" it means that most likely there's some tag and we don't want to touch it.
                if (data.indexOf("<") > -1) {
                    return;
                }
                const unlinkedData = data;

                // Create valid email links (#1761).
                if (data.match(validEmailRegex)) {
                    data = data.replace(
                        validEmailRegex,
                        '<a href="mailto:' +
                            data.replace(doubleQuoteRegex, "%22") +
                            '">$&</a>'
                    );
                    data = tryToEncodeLink(data);
                } else {
                    // https://dev.ckeditor.com/ticket/13419
                    data = data.replace(
                        validUrlRegex,
                        '<a href="' +
                            data.replace(doubleQuoteRegex, "%22") +
                            '">$&</a>'
                    );
                }

                // If link was discovered, change the type to 'html'. This is important e.g. when pasting plain text in Chrome
                // where real type is correctly recognized.
                if (data != unlinkedData) {
                    evt.data.type = "html";
                    evt.data.dataValue = data;
                }
            });

            function tryToEncodeLink(data) {
                // If enabled use link plugin to encode email link.
                if (editor.plugins.link) {
                    var link = CKEDITOR.dom.element.createFromHtml(data),
                        linkData = CKEDITOR.plugins.link.parseLinkAttributes(
                            editor,
                            link
                        ),
                        attributes = CKEDITOR.plugins.link.getLinkAttributes(
                            editor,
                            linkData
                        );

                    if (!CKEDITOR.tools.isEmpty(attributes.set)) {
                        link.setAttributes(attributes.set);
                    }

                    if (attributes.removed.length) {
                        link.removeAttributes(attributes.removed);
                    }
                    return link.getOuterHtml();
                }
                return data;
            }
        }
    });
})();
