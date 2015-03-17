/// <reference path="../../lib/jquery.d.ts" />

// We have a lot of trouble with the embedded Firefox's ContentEdtiable behavior. The chief problem is that FF *really* wants to just use <br>s
// which are worthless, because you can't style them to , for example, do paragraph indents.
// So this module is for various work-arounds. Eventually we should probably declare defeat and move to some existing open-source div editor.

interface JQuery {
    reverse(): JQuery;
}

$.fn.reverse = function () {
    return this.pushStack(this.get().reverse(), arguments);
};

// enhance: we should get some string library available instead of rolling our own

interface String {
    startsWith: (prefix : string) => boolean;
    endsWith: (suffix : string) => boolean;
}

String.prototype.endsWith = function (suffix : string) : boolean {
    return this.indexOf(suffix, this.length - suffix.length) !== -1;
};

String.prototype.startsWith = function (str) :boolean {
    return this.indexOf(str) == 0;
};


function SetupEditableElements(container) {
    //firefox adds a <BR> when you press return, which is lame because you can't put css styles on BR, such as indent.
    //Eventually we may use a wysiwyg add-on which does this conversion as you type, but for now, we change it when
    //you tab or click out.
    $(container).find(".bloom-editable").blur(function () {
        //in the focus event that came long before this blur event, we may have added an empty span to work around a geckobug. Get rid of it now.
        $(this).children("span.bloom-ui").remove();

        //This might mess some things up, so we're only applying it selectively
        if ($(this).closest('.bloom-requiresParagraphs').length == 0
            && ($(this).css('border-top-style') != 'dashed')) //this signal used to let the css add this conversion after some SIL-LEAD SHRP books were already typed
            return;

        var x = $(this).html();

        //the first time we see a field editing in Firefox, it won't have a p opener
        if (!x.trim().startsWith('<p')
            && !x.trim().startsWith('<div')) { // in cases where we are embedding images inside of bloom-editables, the paragraphs actually have to go at the end, for reason of wrapping. See SHRP C1P4 Pupils Book
            x = "<p class='me-red'>" + x;
        }

        x = x.split("<br>").join("</p><p class='me-fromBR'>");

        //the first time we see a field editing in Firefox, it won't have a p closer
        if (!x.trim().endsWith('</p>')) {
            x = x + "</p>";
        }
        $(this).html(x.trim());

        //If somehow you get leading empty paragraphs, FF won't let you delete them
        //        $(this).find('p').each(function () {
        //            if ($(this).text() === "") {
        //                $(this).remove();
        //            } else {
        //                return false; //break
        //            }
        //        });

        //for some reason, perhaps FF-related, we end up with a new empty paragraph each time
        //so remove trailing <p></p>s
        $(this).find('p').reverse().each(function () {
            if ($(this).text() === "") {
                $(this).remove();
            } else {
                return false; //break
            }
        });
    });

//when we discover an empty text box that has been marked to use paragraphs, start us off on the right foot
    $(container).find('.bloom-editable').focus(function () {
        //enhance: we actually want everything to be done with paragraphs, but need to wait for the ReaderTools to be enhanced to cope with that.

        var requireParagraphs = $(this).closest('.bloom-requiresParagraphs').length > 0
            || ($(this).css('border-top-style') == 'dashed'); //this signal used to let the css add this conversion after some SIL-LEAD SHRP books were already typed

        if (!requireParagraphs) {
            // Work around a bug in geckofx. The effect was that if you clicked in a completely empty text box
            // the cursor is oddly positioned and typing does nothing. There is evidence that what is going on is that the focus
            // is on the English qtip (in the FF inspector, the qtip block highlights when you type). https://jira.sil.org/browse/BL-786
            // This bug mentions the cursor being in the wrong place: https://bugzilla.mozilla.org/show_bug.cgi?id=904846
            // so the solution is just to insert a span that you can't see, here during the focus event.
            // Then, we remove that span in the blur event.
            if ($(this).text() == '') {
                //add a span with only a zero-width space in it
                //enhance: a zero-width placeholder would be a bit better, but libsynphony doesn't know this is a space: //$(this).html('<span class="bloom-ui">&#8203;</span>');
                $(this).html('&nbsp;');
                //now we tried deleting it immediatly, or after a pause, but that doesn't help. So now we don't delete it until they type or paste something.
                $(container).find(".bloom-editable").one('paste keypress', FixUpOnFirstInput);
            }
            return;
        }

        if ($(this).text() == '' && $(this).find("p").length == 0) {
            //stick in a paragraph, which makes FF do paragraphs instead of BRs.
            $(this).html('<p class="me - initial">&nbsp;</p>');
            // &zwnj; (zero width non-joiner) would be better but it makes the cursor invisible
            //now select that space, so we delete it when we start typing

            var el = $(this).find('p')[0].childNodes[0];
            var range = document.createRange();
            range.selectNodeContents(el);
            var sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        } else {
            var p = $(this).find('p')[0];
            if (!p)
                return; // these have text, but not p's yet. We'll have to wait until they leave (blur) to add in the P's.
            var range = document.createRange();
            range.selectNodeContents(p);
            range.collapse(true); //move to start of first paragraph
            var sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }
        //TODO if you do Ctrl+A and delete, you're now outside of our <p></p> zone. clicking out will trigger the blur handerl above, which will restore it.
    });

    PreventRemovalOfSomeElements(container);
}

// Some custom templates have image containers embedded in bloom-editable divs, so that the text can wrap
// around the picture. The problems is that the user can do (ctrl+a, del) to start over on the text, and
// inadvertantly remove the embedded images. So we introduced the "bloom-preventRemoval" class, and this
// tries to safeguard element bearing that class.
function PreventRemovalOfSomeElements(container) {

    /* this approach showed promise, but only the first time you do ctrl+all, DEL. After the undo, the bindings were not redone.
     $(container).find(".bloom-preventRemoval").bind("DOMNodeRemoved", function (e) {
     alert("Removed: " + e.target.nodeName);
     //this threw a NS_ERROR but I don't know why
     //document.execCommand('undo', false, null);
     });

     the problem with this one is the event was raised when we weren't actually deleting it
     $(container).bind("DOMNodeRemoved", function (e) {
     if ($(e.target).hasClass('bloom-preventRemoval')) {
     alert("Removed: " + e.target.nodeName);
     document.execCommand('undo', false, null);
     }
     //this threw a NS_ERROR but I don't know why
     //document.execCommand('undo', false, null);
     });
     */


    $(container).find(".bloom-preventRemoval").closest(".bloom-editable").each(function () {
        var numberThatShouldBeThere = $(this).find(".bloom-preventRemoval").length;
        //Note, the input event is *not* fired on the element itself in the (ctrl+a, del) scenario, hence
        //the need to go up to the parent editable and attach the event their.
        $(this).on("input", function (e) {
            if ($(this).find(".bloom-preventRemoval").length < numberThatShouldBeThere) {
                document.execCommand('undo');
            }
        });
    });

    //OK, now what if the above fails in some scenario? This adds a last-resort way of getting
    //bloom-editable back to the state it was in when the page was first created, by having
    //the user type in RESETRESET and then clicking out of the field.
    $(container).find(".bloom-editable").blur(function (e) {
        if ($(this).html().indexOf('RESETRESET') > -1) {
            $(this).remove();
            alert("Now go to another book, then back to this book and page.");
        }
    });
}

interface Selection {
    //This is nonstandard, but supported by firefox. So we have to tell typescript about it
    modify(alter : string, direction : string, granularity : string): Selection;
}

//Earlier, to work around a FF bug, we made a text box non-empty so that the cursor would should up correctly.
//Now, they have entered something, so remove it
function FixUpOnFirstInput() {
    //when this was wired up, we used ".one()", but actually we're getting multiple calls for some reason,
    //and that gets characters in the wrong place because this messes with the insertion point. So now
    //we check to see if the space is still there before touching it
    if ($(this).html().indexOf("&nbsp;") == 0) {
        //earlier we stuck a &nbsp; in to work around a FF bug on empty boxes.
        //now remove it a soon as they type something


        // this caused BL-933 by somehow making us lose the on click event link on the formatButton
        //   $(this).html($(this).html().replace('&nbsp;', ""));

        //so now we do the follow business, where we select the &nbsp; we want to delete, momements before the character is typed or text pasted
        var selection = window.getSelection();

        //if we're at the start of the text, we're to the left of the character we want to replace
        if (selection.anchorOffset == 0) {
            selection.modify("extend", "forward", "character");
            //REVIEW: I actually don't know why this is necessary; the pending keypress should do the same thing
            //But BL-952 showed that without it, we actually somehow end up selecting the format gear icon as well
            selection.deleteFromDocument();
        }
        //if we're at position 1 in the text, then we're just to the right of the character we want to replace
        else if (selection.anchorOffset == 1) {
            selection.modify("extend", "backward", "character");
        }
    }
}
