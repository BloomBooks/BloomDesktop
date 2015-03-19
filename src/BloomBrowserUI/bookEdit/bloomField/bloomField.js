/// <reference path="../../lib/jquery.d.ts" />
// We have a lot of trouble with the embedded Firefox's ContentEditable behavior. The chief problem is that FF *really* wants to just use <br>s,
// which are worthless because you can't style them to, for example, do paragraph indents.
// So this module is for various work-arounds. Eventually we should probably admit defeat and move to some existing open-source div editor.
//Firefox adds a <BR> when you press return, which is lame because you can't put css styles on BR, such as indent.
//Eventually we may use a wysiwyg add-on which does this conversion as you type, but for now, we change it when
//you tab or click out.
var CursorPosition;
(function (CursorPosition) {
    CursorPosition[CursorPosition["start"] = 0] = "start";
    CursorPosition[CursorPosition["end"] = 1] = "end";
})(CursorPosition || (CursorPosition = {}));

var BloomField = (function () {
    function BloomField() {
    }
    BloomField.ManageField = function (target) {
        BloomField.PreventRemovalOfSomeElements(target);

        if (BloomField.RequiresParagraphs(target)) {
            BloomField.ModifyForParagraphMode(target);
            BloomField.ManageWhatHappensIfTheyDeleteEverything(target);
            BloomField.PreventArrowingOutIntoField(target);
            $(target).blur(function () {
                BloomField.ModifyForParagraphMode(this);
            });
            $(target).focusin(function () {
                BloomField.HandleFieldFocus(this);
            });
        } else {
            BloomField.PrepareNonParagraphField(target);
        }
    };

    // Without this, ctrl+a followed by a left-arrow or right-arrow gets you out of all paragraphs,
    // so you can start messing things up.
    BloomField.PreventArrowingOutIntoField = function (field) {
        $(field).keydown(function (e) {
            var leftArrowPressed = e.which === 37;
            var rightArrowPressed = e.which === 39;
            if (leftArrowPressed || rightArrowPressed) {
                var sel = window.getSelection();
                if (sel.anchorNode === this) {
                    //enhance: the correct behavior would actually be to collapse the selection to one point at the beginning
                    e.preventDefault();
                    BloomField.MoveCursorToFirstParagraph(this, leftArrowPressed ? 0 /* start */ : 1 /* end */);
                }
            }
        });
    };

    BloomField.EnsureStartsWithParagraphElement = function (field) {
        if ($(field).children().length > 0 && ($(field).children().first().prop("tagName").toLowerCase() === 'p')) {
            return;
        }
        $(field).prepend('<p></p>');
    };

    BloomField.EnsureEndsWithParagraphElement = function (field) {
        //Enhance: move any errant paragraphs to after the imageContainer
        if ($(field).children().length > 0 && ($(field).children().last().prop("tagName").toLowerCase() === 'p')) {
            return;
        }
        $(field).append('<p></p>');
    };

    BloomField.ConvertTextNodesToParagraphs = function (field) {
        var nodes = field.childNodes;
        for (var n = 0; n < nodes.length; n++) {
            var node = nodes[n];
            if (node.nodeType === 3) {
                var paragraph = document.createElement('p');
                if (node.textContent.trim() !== '') {
                    paragraph.textContent = node.textContent;
                    node.parentNode.insertBefore(paragraph, node);
                }
                node.parentNode.removeChild(node);
            }
        }
    };

    // We expect that once we're in paragraph mode, there will not be any cleanup needed. However, there
    // are three cases where we have some conversion to do:
    // 1) when a field is totally empty, we need to actually put in a <p> into the empty field (else their first
    //      text doesn't get any of the formatting assigned to paragraphs)
    // 2) when this field was already used by the user, and then later switched to paragraph mode.
    // 3) corner cases that aren't handled by as-you-edit events. E.g., pressing "ctrl+a DEL"
    BloomField.ModifyForParagraphMode = function (field) {
        BloomField.ConvertTextNodesToParagraphs(field);
        $(field).find('br').remove();

        // in cases where we are embedding images inside of bloom-editables, the paragraphs actually have to go at the
        // end, for reason of wrapping. See SHRP C1P4 Pupils Book
        //if(x.startsWith('<div')){
        if ($(field).find('.bloom-keepFirstInField').length > 0) {
            BloomField.EnsureEndsWithParagraphElement(field);
            return;
        } else {
            BloomField.EnsureStartsWithParagraphElement(field);
        }
    };

    BloomField.RequiresParagraphs = function (field) {
        return $(field).closest('.bloom-requiresParagraphs').length > 0 || ($(field).css('border-top-style') === 'dashed');
    };

    BloomField.HandleFieldFocus = function (field) {
        BloomField.MoveCursorToFirstParagraph(field, 0 /* start */);
        return;

        if (!BloomField.RequiresParagraphs(field)) {
            return;
        }
        if ($(field).text() === '' && $(field).find("p").length === 0) {
            //stick in a paragraph, which makes FF do paragraphs instead of BRs.
            $(field).html('<p class="me - initial">&nbsp;</p>');

            // &zwnj; (zero width non-joiner) would be better but it makes the cursor invisible
            //now select that space, so we delete it when we start typing
            var el = $(field).find('p')[0].childNodes[0];
            var range = document.createRange();
            range.selectNodeContents(el);
            var sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        } else {
            var p = $(field).find('p')[0];
            if (!p) {
                return;
            }
            var range = document.createRange();
            range.selectNodeContents(p);
            range.collapse(true); //move to start of first paragraph

            var sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }
    };

    BloomField.MoveCursorToFirstParagraph = function (field, position) {
        var range = document.createRange();
        if (position === 0 /* start */) {
            range.selectNodeContents($(field).find('p').first()[0]);
        } else {
            range.selectNodeContents($(field).find('p').last()[0]);
        }
        range.collapse(position === 0 /* start */); //true puts it at the start
        var sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
    };
    BloomField.ManageWhatHappensIfTheyDeleteEverything = function (field) {
        // if the user types (ctrl+a, del) then we get an empty element or '<br></br>', and need to get a <p> in there.
        // if the user types (ctrl+a, 'blah'), then we get
        $(field).on("input", function (e) {
            if ($(this).find('p').length === 0) {
                BloomField.ModifyForParagraphMode(this);

                // Now put the cursor in the paragraph, *after* the character they may have just typed or the
                // text they just pasted.
                BloomField.MoveCursorToFirstParagraph(field, 1 /* end */);
            }
        });
    };

    // Some custom templates have image containers embedded in bloom-editable divs, so that the text can wrap
    // around the picture. The problems is that the user can do (ctrl+a, del) to start over on the text, and
    // inadvertently remove the embedded images. So we introduced the "bloom-preventRemoval" class, and this
    // tries to safeguard element bearing that class.
    BloomField.PreventRemovalOfSomeElements = function (field) {
        var numberThatShouldBeThere = $(field).find(".bloom-preventRemoval").length;
        if (numberThatShouldBeThere > 0) {
            $(field).on("input", function (e) {
                if ($(this).find(".bloom-preventRemoval").length < numberThatShouldBeThere) {
                    document.execCommand('undo');
                    e.preventDefault();
                }
            });
        }

        //OK, now what if the above fails in some scenario? This adds a last-resort way of getting
        //bloom-editable back to the state it was in when the page was first created, by having
        //the user type in RESETRESET and then clicking out of the field.
        $(field).blur(function (e) {
            if ($(this).html().indexOf('RESETRESET') > -1) {
                $(this).remove();
                alert("Now go to another book, then back to this book and page.");
            }
        });
    };

    // Work around a bug in geckofx. The effect was that if you clicked in a completely empty text box
    // the cursor is oddly positioned and typing does nothing. There is evidence that what is going on is that the focus
    // is on the English qtip (in the FF inspector, the qtip block highlights when you type). https://jira.sil.org/browse/BL-786
    // This bug mentions the cursor being in the wrong place: https://bugzilla.mozilla.org/show_bug.cgi?id=904846
    BloomField.PrepareNonParagraphField = function (field) {
        if ($(field).text() === '') {
            //add a span with only a zero-width space in it
            //enhance: a zero-width placeholder would be a bit better, but libsynphony doesn't know this is a space: //$(this).html('<span class="bloom-ui">&#8203;</span>');
            $(field).html('&nbsp;');

            //now we tried deleting it immediately, or after a pause, but that doesn't help. So now we don't delete it until they type or paste something.
            // REMOVE: why was this doing it for all of the elements? $(container).find(".bloom-editable").one('paste keypress', FixUpOnFirstInput);
            $(field).one('paste keypress', this.FixUpOnFirstInput);
        }
    };

    //In PrepareNonParagraphField(), to work around a FF bug, we made a text box non-empty so that the cursor would should up correctly.
    //Now, they have entered something, so remove it
    BloomField.FixUpOnFirstInput = function (event) {
        var field = event.target;

        //when this was wired up, we used ".one()", but actually we're getting multiple calls for some reason,
        //and that gets characters in the wrong place because this messes with the insertion point. So now
        //we check to see if the space is still there before touching it
        if ($(field).html().indexOf("&nbsp;") === 0) {
            //earlier we stuck a &nbsp; in to work around a FF bug on empty boxes.
            //now remove it a soon as they type something
            // this caused BL-933 by somehow making us lose the on click event link on the formatButton
            //   $(this).html($(this).html().replace('&nbsp;', ""));
            //so now we do the follow business, where we select the &nbsp; we want to delete, momements before the character is typed or text pasted
            var selection = window.getSelection();

            //if we're at the start of the text, we're to the left of the character we want to replace
            if (selection.anchorOffset === 0) {
                selection.modify("extend", "forward", "character");

                //REVIEW: I actually don't know why this is necessary; the pending keypress should do the same thing
                //But BL-952 showed that without it, we actually somehow end up selecting the format gear icon as well
                selection.deleteFromDocument();
            } else if (selection.anchorOffset === 1) {
                selection.modify("extend", "backward", "character");
            }
        }
    };
    return BloomField;
})();
//# sourceMappingURL=bloomField.js.map
