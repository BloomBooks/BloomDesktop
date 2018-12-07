/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../typings/ckeditor/ckeditor.d.ts" />

// This class is actually just a group of static functions with a single public method. It does whatever we need to to make Firefox's contenteditable
// element have the behavior we need.
//
// For example, we have trouble with FF's ability to show a cursor in a box that has an :after content but no text content. So here we work around
// that.
//
// Next, we need to support fields that are made of paragraphs. But FF *really* wants to just use <br>s,
// which are worthless because you can't style them. You can't, for example, do paragraph indents or have :before content.
// So the first thing we do here is to work-around that limitation. So we prepare for working in paragraphs, keep you in paragraphs while
// editing, and make sure all is ok when you leave.
//
// Next, our field templates need to have embedded images that text can flow around. To allow that, we have to keep the p elements *after* the image elements, even
// if visually the text is before and after the text (because that's how the image-pusher-downer technique works (zero-width, float-left
// div of the height you want to push the image down to). We do this by noticing a 'bloom-keepFirstInField' class on some div encapsulating the image.
//
// Next, we have to keep you from accidentally losing the image placeholder when you do ctrl+a DEL. We prevent this deletion
// for any element marked with a 'bloom-preventRemoval' class.

export default class BloomField {
    public static ManageField(bloomEditableDiv: HTMLElement) {
        BloomField.PreventRemovalOfSomeElements(bloomEditableDiv);
        BloomField.ManageWhatHappensIfTheyDeleteEverything(bloomEditableDiv);
        // ManageWhatHappensIfTheyDeleteEverything() is actually enough, it cleans things up. But can still show some momentary cursor
        // weirdness. So at least in this common case, we prevent the backspace altogether.
        BloomField.PreventBackspaceAtStartFromRemovingParagraph(
            bloomEditableDiv
        );
        BloomField.PreventArrowingOutIntoField(bloomEditableDiv);
        BloomField.PreventBackspaceAtStartFromMovingTextIntoEmbeddedImageCaption(
            bloomEditableDiv
        );

        BloomField.MakeShiftEnterInsertLineBreak(bloomEditableDiv);

        // Things assume that the editable will definitely contain one paragraph.
        // If it doesn't, subtle weird things can happen.
        // 1) For example, if a text-over-picture element does not contain a paragraph, you often can't type directly into the box immediately.
        //    (you need to switch focus to a different text box then switch focus back to the text-over-picture)
        // 2) Text-over-picture elements with no paragraph, only a span containing two words on exactly one line will get messed up by CKEditor when the page is saved (including some tool activations may trigger saves)
        // 3) Some styling rules (e.g. indentation) assume it is in paragraphs and probably won't be applied immediately.
        // 4) probably others
        BloomField.ModifyForParagraphMode(bloomEditableDiv);

        //For future: this works, but we need more time to think about it. BloomField.MakeTabEnterTabElement(bloomEditableDiv);

        /*  The following is assumed to not be needed currently (3.9)... probably not needed
            since we added ckeditor.
            However this code is retained because this is *very* expensive code to get these
            kinds of low-level html editor methods working right, so it
            makes sense to leave them here in case we have analogous situations come up.

            BloomField.ModifyForParagraphMode(bloomEditableDiv);
            $(bloomEditableDiv).blur(function () {
                BloomField.ModifyForParagraphMode(this);
            });
            $(bloomEditableDiv).focusin(function () {
                BloomField.HandleFieldFocus(this);
            });
            BloomField.PrepareNonParagraphField(bloomEditableDiv);
            BloomField.ManageWhatHappensIfTheyDeleteEverythingNonParagraph(bloomEditableDiv);
        */
    }

    private static MakeTabEnterTabElement(field: HTMLElement) {
        $(field).keydown(e => {
            if (e.which === 9) {
                //note: some people introduce a new element, <tab>. That has the advantage
                //of having a stylesheet-controllable width. However, Firefox leave the
                //cursor in side of the <tab></tab>, and though we could manage
                //to get out of that, what if someone moved back into it? Etc. etc.
                //So I'm going with the conservative choice for now, which is the em space,
                //which is about as wide as 4 spaces.
                document.execCommand("insertHTML", false, "&emsp;");
                e.stopPropagation();
                e.preventDefault();
            }
        });
    }

    private static InsertLineBreak() {
        //we put in a specially marked span which stylesheets can use to give us "soft return" in the midst of paragraphs
        //which have either indents or prefixes (like "step 1", "step 2").
        //The difficult part is that the browser will leave our cursor inside of the new span, which isn't really
        //what we want. So we also add a zero-width-non-joiner (&#xfeff;) there so that we can get outside of the span.
        document.execCommand(
            "insertHTML",
            false,
            "<span class='bloom-linebreak'></span>&#xfeff;"
        );
    }

    // This BloomField thing was done before ckeditor; ckeditor kinda expects to the only thing
    // taking responsibility for the field, so that creates problems. Eventually, we could probably
    // re-cast everything in this BloomField class as a plugin or at least callbacks to ckeditor.
    // For now, I just want to fix BL-3009, where this class could no longer get access to shift-enter
    // keypresses. To regain access, we have to wire up to ckeditor.
    public static WireToCKEditor(
        bloomEditableDiv: HTMLElement,
        ckeditor: CKEDITOR.editor
    ) {
        ckeditor.on("key", event => {
            if (event.data.keyCode === CKEDITOR.SHIFT + 13) {
                BloomField.InsertLineBreak();
                event.cancel();
            }
        });
        // ckeditor.on('afterPasteFromWord', event => {
        //     alert(event.data.dataValue);
        // });
        ckeditor.on("paste", event => {
            var endMkr = "";
            let paras = event.data.dataValue.match(/<p>/g);
            if (!paras) {
                // Enhance: should we remove the probable <span> and just leave the text?
                // But it might be carrying some important style info.
                return;
            }
            // OK, we are going to unwrap the first <p> and just leave its content.
            // This prevents a typically unwanted extra line break being inserted before the
            // start of the material copied. Without the <p> wrapper, that material just
            // gets inserted into the current paragraph.
            if (paras.length === 1) {
                // Unwrapping the one and only <p> will leave no break between what used to be that
                // paragraph and the following text, which should now start a new paragraph.
                // We insert an empty paragraph to force a break, but that will leave an unwanted
                // empty paragraph! So we have an afterPaste event to remove it.
                endMkr += "<p class='removeMe'></p>";
            }
            // However many paragraphs there are, we will remove the FIRST <p> (if any) and its
            // corresponding </p> (possibly inserting in its place the empty paragraph which
            // will be removed after the paste).
            event.data.dataValue = event.data.dataValue
                .replace("<p>", "")
                .replace("</p>", endMkr);
        });
        ckeditor.on("afterPaste", event => {
            // clean up possible unwanted paragraph inserted by paste event.
            $(".removeMe").remove();
        });

        // This makes it easy to find the right editor instance. There may be some ckeditor built-in way, but
        // I wasn't able to find one.
        (<any>bloomEditableDiv).bloomCkEditor = ckeditor;
    }

    private static MakeShiftEnterInsertLineBreak(field: HTMLElement) {
        $(field).keypress(e => {
            //NB: This will not fire in the (now normal case) that ckeditor is in charge of this field.
            if (e.which === 13) {
                //enter key
                if (e.shiftKey) {
                    BloomField.InsertLineBreak();
                } else {
                    // If the enter didn't come with a shift key, just insert a paragraph.
                    // Now, why are we doing this if firefox would do it anyway? Because if we previously pressed shift - enter
                    // and got that <span class='bloom-linebreak'></span>, firefox will actually insert that span again, in the
                    // new paragraphs (which would be reasonable if we had turned on a normal text-formating style, like a text color.
                    // So we do the paragraph creation ourselves, so that we don't get any unwanted <span>s in it.
                    // Note that this is going to remove that "make new spans automatically" feature entirely.
                    // If we need it someday, we'll have to make this smarter and only override the normal behavior if we can detect
                    // that the span it would create would be one of those bloom-linbreak ones.

                    //The other thing going on is that Firefox doesn't like to see multiple empty <p></p>'s. It won't let us insert
                    //two or more of these in a row. So we stick in a zero-width-non-joiner element to pacify it.
                    //This has the downside that it takes to presses of "DEL" to remove the line; a future enhancement could fix
                    //that.
                    document.execCommand("insertHTML", false, "<p>&zwnj;</p>");
                }
                e.stopPropagation();
                e.preventDefault();
            }
        });
    }

    // This was originally here to do some cleanup needed by SIL-LEAD/SHRP as their
    // typists copied from Word where they had used spaces instead of tabs, too many linebreaks, etc.
    // It was broken when we added ckeditor, and now isn't actually needed. Meanwhile though I
    // make this way to get a special paste by ctrl-clicking on the paste icon and bypassing
    // ckeditor. So I'm leaving this toy example here to save us
    // time if we need to do something similar in the future.
    // JohnT: disabled when we switched to modules, since to make it work again we'd have to
    // work it into FrameExports etc.
    //public static CalledByCSharp_SpecialPaste(contents: string) {
    //    let html = contents.replace(/[b,c,d,f,g,h,j,k,l,m,n,p,q,r,s,t,v,w,x,z]/g, 'C');
    //    html = html.replace(/[a,e,i,o,u]/g, 'V');
    //    //convert newlines to paragraphs. We're already inside a  <p>, so each
    //    //newline finishes that off and starts a new one
    //    html = html.replace(/\n/g, '</p><p>');
    //    var page = <HTMLIFrameElement>document.getElementById('page');
    //    page.contentWindow.document.execCommand("insertHTML", false, html);
    //}

    // Since embedded images come before the first editable text, going to the beginning of the field and pressing Backspace moves the current paragraph into the caption. Sigh.
    private static PreventBackspaceAtStartFromMovingTextIntoEmbeddedImageCaption(
        field: HTMLElement
    ) {
        if (
            $(field).find(".bloom-keepFirstInField.bloom-preventRemoval")
                .length == 0
        ) {
            return;
        }

        var divToProtect = $(field).find(
            ".bloom-keepFirstInField.bloom-preventRemoval"
        )[0];

        //We have this to fix up cases existing before we introduced this prevention, and also
        // as a backup plan, in case there is some way we haven't discovered to bypass the
        //prevention algorithm below.

        //The following checks the top level elemenents and only allows divs; the two items
        //that we expect in there are the div for the "imagePusherDowner" and the div for
        //the image - container(which in turn contains the caption).
        $(divToProtect)
            .children()
            .filter(function() {
                return this.localName.toLowerCase() != "div";
            })
            .each(() => {
                //divToProtect.removeChild(this);
            });
        //also remove any raw text nodes, which you can only get at with "contents"
        //note this is still only one level deep, so it doesn't endanger the caption
        $(divToProtect)
            .contents()
            .filter(function() {
                return (
                    this.nodeType == Node.TEXT_NODE &&
                    this.textContent.trim().length > 0
                );
            })
            .each(() => {
                // divToProtect.removeChild(this);
            });

        //Enhance: Currently, this will prevent backspacing sometimes when it should be OK. Specifically,
        //If we are in the first paragraph and the cursor is to the left of the first character of another
        //element  (<b>, <i>, <span>, etc.), then we'll have a false positive because sel.anchorOffset will
        //be 0. To really solve this, we would need to be able to determine if we are in the first text node
        //of the paragraph, because that's the case where FF will try and remove the  P and move it into the
        //preceding div.
        $(field).keydown(e => {
            if (e.which == 8 /* backspace*/) {
                var sel = window.getSelection();
                //Are we at the start of a paragraph with nothing selected?
                if (sel.anchorOffset == 0 && sel.isCollapsed) {
                    //Are we in the first paragraph?
                    //Embedded image divs come before the first editable paragraph, so we look at the previous element and
                    //see if it is one those. Anything marked with bloom-preventRemoval is probably not something we want to
                    //be merging with.
                    var previousElement = $(sel.anchorNode)
                        .closest("P")
                        .prev();
                    if (
                        previousElement.length > 0 &&
                        previousElement[0] == divToProtect
                    ) {
                        e.stopPropagation();
                        e.preventDefault();
                        console.log("Prevented Backspace");
                    }
                }
            }
        });
    }

    // Without this, ctrl+a followed by a left-arrow or right-arrow gets you out of all paragraphs,
    // so you can start messing things up.
    private static PreventArrowingOutIntoField(field: HTMLElement) {
        $(field).keydown(function(e) {
            var leftArrowPressed = e.which === 37;
            var rightArrowPressed = e.which === 39;
            if (leftArrowPressed || rightArrowPressed) {
                var sel = window.getSelection();
                if (sel.anchorNode === this) {
                    e.preventDefault();
                    BloomField.MoveCursorToEdgeOfField(
                        this,
                        leftArrowPressed
                            ? CursorPosition.start
                            : CursorPosition.end
                    );
                }
            }
        });
    }

    private static EnsureStartsWithParagraphElement(field: HTMLElement) {
        if (
            $(field).children().length > 0 &&
            $(field)
                .children()
                .first()
                .prop("tagName")
                .toLowerCase() === "p"
        ) {
            return;
        }
        $(field).prepend("<p></p>");
    }

    private static EnsureEndsWithParagraphElement(field: HTMLElement) {
        //Enhance: move any errant paragraphs to after the imageContainer
        if (
            $(field).children().length > 0 &&
            $(field)
                .children()
                .last()
                .prop("tagName")
                .toLowerCase() === "p"
        ) {
            return;
        }
        $(field).append("<p></p>");
    }

    private static ConvertTopLevelTextNodesToParagraphs(field: HTMLElement) {
        //enhance: this will leave <span>'s that are direct children alone; ideally we would incorporate those into paragraphs
        var nodes = field.childNodes;
        for (var n = 0; n < nodes.length; n++) {
            var node = nodes[n];
            if (node.nodeType === 3) {
                //Node.TEXT_NODE
                var paragraph = document.createElement("p");
                if (
                    node.textContent != null &&
                    node.textContent.trim() !== ""
                ) {
                    paragraph.textContent = node.textContent;
                    if (node.parentNode != null)
                        node.parentNode.insertBefore(paragraph, node);
                }
                if (node.parentNode != null) node.parentNode.removeChild(node);
            }
        }
    }

    // We expect that once we're in paragraph mode, there will not be any cleanup needed. However, there
    // are three cases where we have some conversion to do:
    // 1) when a field is totally empty, we need to actually put in a <p> into the empty field (else their first
    //      text doesn't get any of the formatting assigned to paragraphs)
    // 2) when this field was already used by the user, and then later switched to paragraph mode.
    // 3) corner cases that aren't handled by as-you-edit events. E.g., pressing "ctrl+a DEL"
    private static ModifyForParagraphMode(field: HTMLElement) {
        BloomField.ConvertTopLevelTextNodesToParagraphs(field);
        $(field)
            .find("br")
            .remove();

        // in cases where we are embedding images inside of bloom-editables, the paragraphs actually have to go at the
        // end, for reason of wrapping. See SHRP C1P4 Pupils Book
        //if(x.startsWith('<div')){
        if ($(field).find(".bloom-keepFirstInField").length > 0) {
            BloomField.EnsureEndsWithParagraphElement(field);
            return;
        } else {
            BloomField.EnsureStartsWithParagraphElement(field);
        }
    }

    /* Currently unused. See note in ManageField().
    private static HandleFieldFocus(field: HTMLElement) {
        BloomField.MoveCursorToEdgeOfField(field, CursorPosition.start);
    }
    */

    private static MoveCursorToEdgeOfField(
        field: HTMLElement,
        position: CursorPosition
    ) {
        var range = document.createRange();
        if (position === CursorPosition.start) {
            range.selectNodeContents(
                $(field)
                    .find("p")
                    .first()[0]
            );
        } else {
            range.selectNodeContents(
                $(field)
                    .find("p")
                    .last()[0]
            );
        }
        range.collapse(position === CursorPosition.start); //true puts it at the start
        var sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
    }

    private static ManageWhatHappensIfTheyDeleteEverything(field: HTMLElement) {
        // if the user types (ctrl+a, del) then we get an empty element or '<br></br>', and need to get a <p> in there.
        // if the user types (ctrl+a, 'blah'), then we get blah outside of any paragraph

        $(field).keyup(e => {
            if ($(this).find("p").length === 0) {
                BloomField.ModifyForParagraphMode(field);

                // Now put the cursor in the paragraph, *after* the character they may have just typed or the
                // text they just pasted.
                BloomField.MoveCursorToEdgeOfField(field, CursorPosition.end);
            }
        });
    }

    // Some custom templates have image containers embedded in bloom-editable divs, so that the text can wrap
    // around the picture. The problem is that the user can do (ctrl+a, del) to start over on the text, and
    // inadvertently remove the embedded images. So we introduced the "bloom-preventRemoval" class, and this
    // tries to safeguard elements bearing that class.
    private static PreventRemovalOfSomeElements(field: HTMLElement) {
        var numberThatShouldBeThere = $(field).find(".bloom-preventRemoval")
            .length;
        if (numberThatShouldBeThere > 0) {
            $(field).keyup(e => {
                if (
                    $(this).find(".bloom-preventRemoval").length <
                    numberThatShouldBeThere
                ) {
                    document.execCommand("undo");
                    e.preventDefault();
                }
            });
        }

        //OK, now what if the above fails in some scenario? This adds a last-resort way of getting
        //bloom-editable back to the state it was in when the page was first created, by having
        //the user type in RESETRESET and then clicking out of the field.
        // Since the elements that should not be deleted are part of a parallel field in a
        // template language, initial page setup will copy it into a new version of the messed
        // up one if the relevant language version is missing altogether
        $(field).blur(function(e) {
            if (
                $(this)
                    .html()
                    .indexOf("RESETRESET") > -1
            ) {
                $(this).remove();
                alert(
                    "Now go to another book, then back to this book and page."
                );
            }
        });
    }

    // Work around a bug in geckofx. The effect was that if you clicked in a completely empty text box
    // the cursor is oddly positioned and typing does nothing. There is evidence that what is going on is that the focus
    // is on the English qtip (in the FF inspector, the qtip block highlights when you type). https://jira.sil.org/browse/BL-786
    // This bug mentions the cursor being in the wrong place: https://bugzilla.mozilla.org/show_bug.cgi?id=904846
    // The reason this is for "non paragraph fields" is that these are the only kind that can be empty. Fields with <p>'s are never
    // totally empty, so they escape this bug.
    // private static PrepareNonParagraphField(field: HTMLElement) {
    //     if ($(field).text() === '') {
    //         //add a span with only a zero-width space in it
    //         //enhance: a zero-width placeholder would be a bit better, but theOneLibSynphony doesn't know this is a space: //$(this).html('<span class="bloom-ui">&#8203;</span>');
    //         $(field).html('&nbsp;');
    //         //now we tried deleting it immediately, or after a pause, but that doesn't help. So now we don't delete it until they type or paste something.
    //         // REMOVE: why was this doing it for all of the elements? $(container).find(".bloom-editable").one('paste keypress', FixUpOnFirstInput);
    //         $(field).one('paste keypress', this.FixUpOnFirstInput);
    //     }
    // }

    // private static ManageWhatHappensIfTheyDeleteEverythingNonParagraph(field: HTMLElement) {
    //     // if the user deletes everthing then we get an empty element, and we may need the bug work around described above.
    //     // see https://silbloom.myjetbrains.com/youtrack/issue/BL-2274.
    //     $(field).on("input", function (e) {
    //         BloomField.PrepareNonParagraphField(this);
    //     });
    // }

    //In PrepareNonParagraphField(), to work around a FF bug, we made a text box non-empty so that the cursor would show up correctly.
    //Now, they have entered something, so remove it
    private static FixUpOnFirstInput(event: any) {
        var field = event.target;
        //when this was wired up, we used ".one()", but actually we're getting multiple calls for some reason,
        //and that gets characters in the wrong place because this messes with the insertion point. So now
        //we check to see if the space is still there before touching it
        if (
            $(field)
                .html()
                .indexOf("&nbsp;") === 0
        ) {
            //earlier we stuck a &nbsp; in to work around a FF bug on empty boxes.
            //now remove it a soon as they type something

            // this caused BL-933 by somehow making us lose the on click event link on the formatButton
            //   $(this).html($(this).html().replace('&nbsp;', ""));

            //so now we do the following business, where we select the &nbsp; we want to delete, moments before the character is typed or text pasted

            var selection: FFSelection = window.getSelection() as FFSelection;

            //if we're at the start of the text, we're to the left of the character we want to replace
            if (selection.anchorOffset === 0) {
                var doNotDeleteOrMove = false;
                // if we've typed a backspace, delete, or arrow key, don't do it and call this method again next time.
                // see https://silbloom.myjetbrains.com/youtrack/issue/BL-2274.
                if (typeof event.charCode == "number" && event.charCode == 0) {
                    doNotDeleteOrMove =
                        event.keyCode == 8 /*backspace*/ ||
                        event.keyCode == 46 /*delete*/ ||
                        event.keyCode == 37 /*left arrow*/ ||
                        event.keyCode == 38 /*up arrow*/ ||
                        event.keyCode == 39 /*right arrow*/ ||
                        event.keyCode == 40 /*down arrow*/;
                }
                if (doNotDeleteOrMove) {
                    event.stopImmediatePropagation();
                    event.stopPropagation();
                    $(field).one("paste keypress", this.FixUpOnFirstInput);
                } else {
                    selection.modify("extend", "forward", "character");
                    //REVIEW: I actually don't know why this is necessary; the pending keypress should do the same thing
                    //But BL-952 showed that without it, we actually somehow end up selecting the format gear icon as well
                    selection.deleteFromDocument();
                }
            } //if we're at position 1 in the text, then we're just to the right of the character we want to replace
            else if (selection.anchorOffset === 1) {
                selection.modify("extend", "backward", "character");
            }
        }
    }

    private static PreventBackspaceAtStartFromRemovingParagraph(
        field: HTMLElement
    ) {
        $(field).keydown(e => {
            if (e.which === 8 /* backspace*/) {
                var sel = window.getSelection();
                //Are we at the start of a paragraph with nothing selected?
                if (sel.anchorOffset == 0 && sel.isCollapsed) {
                    //Are we in the first paragraph?
                    var previousElement = $(sel.anchorNode)
                        .closest("P")
                        .prev();
                    if (previousElement.length == 0) {
                        e.stopPropagation();
                        e.preventDefault();
                        console.log("Prevented Backspace");
                    }
                }
            }
        });
    }
}
enum CursorPosition {
    start,
    end
}
interface FFSelection extends Selection {
    //This is nonstandard, but supported by firefox. So we have to tell typescript about it
    modify(alter: string, direction: string, granularity: string): Selection;
}
interface JQuery {
    reverse(): JQuery;
}
