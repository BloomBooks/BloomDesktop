/**
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

                // If we found "<" it means that most likely there's some tag and we don't want to touch it.
                // Chrome gratuitously wraps the copied data with "<!--StartFragment-->" and "<!--EndFragment-->"
                // when copying from the address bar.  Copying from inside VS-Code does the same on Windows but
                // also adds "<p class='removeMe'></p>" at the end of the data before that even on Linux.  On
                // Linux, these two sources also terminate the data with "\u0000".
                // These shouldn't prevent the paste from automatically inserting a link.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-6845.
                if (
                    data.startsWith("<!--StartFragment-->") &&
                    data.endsWith("<!--EndFragment-->")
                ) {
                    data = data.substring(20);
                    data = data.substring(0, data.length - 18);
                }
                if (data.endsWith("\u0000")) {
                    data = data.substring(0, data.length - "\u0000".length);
                }
                if (data.endsWith("<p class='removeMe'></p>")) {
                    data = data.substring(0, data.length - 24);
                }
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
