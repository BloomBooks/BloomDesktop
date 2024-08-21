/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../typings/ckeditor/ckeditor.d.ts" />

import AudioRecording from "../toolbox/talkingBook/audioRecording";
import { get, post } from "../../utils/bloomApi";
import BloomMessageBoxSupport from "../../utils/bloomMessageBoxSupport";

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

        //For future: this works, but we need more time to think about it. BloomField.MakeTabEnterTabElement(bloomEditableDiv);

        /*  The following is assumed to not be needed currently (3.9)... probably not needed
            since we added ckeditor.
            However this code is retained because this is *very* expensive code to get these
            kinds of low-level html editor methods working right, so it
            makes sense to leave them here in case we have analogous situations come up.

            BloomField.EnsureParagraphsPresent(bloomEditableDiv);
            $(bloomEditableDiv).blur(function () {
                BloomField.ModifyForParagraphMode(this);
            });
            $(bloomEditableDiv).focusin(function () {
                BloomField.HandleFieldFocus(this);
            });
            BloomField.PrepareNonParagraphField(bloomEditableDiv);
            BloomField.ManageWhatHappensIfTheyDeleteEverythingNonParagraph(bloomEditableDiv);
        */

        // What this does is mostly redundant with things CkEditor does, but in some cases CkEditor inserts more paragraphs than we want when we start out with none. See BL-6721"
        //
        // Things assume that the editable will definitely contain one paragraph.
        // If it doesn't, subtle weird things can happen.
        // 1) For example, if a text-over-picture element does not contain a paragraph, you often can't type directly into the box immediately.
        //    (you need to switch focus to a different text box then switch focus back to the text-over-picture)
        // 2) Text-over-picture elements with no paragraph, only a span containing two words on exactly one line will get messed up by CKEditor when the page is saved (including some tool activations may trigger saves)
        // 3) Some styling rules (e.g. indentation) assume it is in paragraphs and probably won't be applied immediately.
        // 4) probably others
        BloomField.EnsureParagraphsPresent(bloomEditableDiv);
    }

    private static MakeTabEnterTabElement(field: HTMLElement) {
        $(field).keydown(e => {
            if (e.key === "Tab") {
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
        // We put in a specially marked span which stylesheets can use to give us "soft return" in the
        // midst of paragraphs which have either indents or prefixes (like "step 1", "step 2").
        // We had to move away from deprecated code (execCommand), when it didn't work in WebView2.
        // Browsers still support execCommand, of course, but they've never agreed on how to implement the
        // various commands, which is why we have a different result using the same command and a new
        // browser engine.
        const sel = window.getSelection();
        if (!sel) return;
        const range = sel.getRangeAt(0);
        const spanToInsert = document.createElement("span");
        spanToInsert.className = "bloom-linebreak";
        range.deleteContents(); // If the user has selected a range, we replace it with the line break.
        range.insertNode(spanToInsert);
        if (!spanToInsert.nextSibling?.textContent) {
            // Without inserting something, the cursor will be before the span,
            // which is not what we want.  A visible character like _ works just
            // as well as a zero-width space, but which is easier for users to
            // deal with is a good question.
            spanToInsert.insertAdjacentText("afterend", "\u200C"); //&zwnj;
        }
        sel.collapseToEnd(); // moves the cursor to after the line break
    }

    public static fixPasteData(input: string): string {
        // Deal with sources that still use <b> and <i> instead of <strong> and <em>.
        // Check if any of these markers are present, and if so, fix all of them.
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-8711.
        if (/<\/?[bi]>/i.test(input)) {
            // Bizarrely, normal text copied from a google doc arrives as
            // a <b> element with a style attribute setting font-weight normal.
            // To allow us to prevent this showing up as bold in Bloom, or
            // duplicating the bizarre markup, we configure CkEditor's pasteFilter
            // (see config.js) to allow the font-weight style. (It won't allow
            // any other styles or attributes through, which simplifies the regex.)
            // If a bold element specifies font-weight:normal, we just remove it
            // altogether (keeping only its content).
            // For somewhat more completeness, we also treat font weights starting with
            // a number less than 5 as normal.
            // This code is intended to handle cases where there might be multiple
            // such runs in the dataValue, but I haven't been able to produce such
            // a case by copying from a google doc. Copying a bold word produces
            // a <b> element...with font-weight:normal!! Copying a run of text in
            // which some bold text is embedded produces...a single <b> element
            // with font-weight:normal. So it seems there is no way to successfully
            // copy text from a google doc and preserve boldness, but at least we
            // can avoid spuriously introducing it.
            const fixGoogleDocNormal = input.replace(
                /<b style="font-weight: *(normal|[1234]).*?>(.*?)<\/b *>/gi,
                "$2"
            );
            // In order to handle the google doc bizarreness, we allow bold elements to have style
            // But we don't want it unless it is the google doc case we care about.
            const fixBoldStyle = fixGoogleDocNormal.replace(
                /<b style=".*?>/gi,
                "<b>"
            );
            const fixBold = fixBoldStyle.replace(/<(\/?)b>/gi, "<$1strong>");
            const fixItalic = fixBold.replace(/<(\/?)i>/gi, "<$1em>");
            return fixItalic;
        }
        return input;
    }

    public static removeUselessSpanMarkup(input: string): string {
        // Paste data from Word can have span elements with no attributes.
        // These serve no purpose and can cause problems in Bloom audio
        // highlighting after splitting with aeneas.  We remove them here.
        // See BL-12861.
        if (!input.includes("<span>")) return input;
        const newDiv = document.createElement("div");
        newDiv.innerHTML = input;
        const spans = newDiv.getElementsByTagName("span");
        for (let i = spans.length - 1; i >= 0; i--) {
            const span = spans[i];
            if (!span.hasAttributes()) {
                const spanContent: (string | Node)[] = [];
                span.childNodes.forEach(child => {
                    spanContent.push(child);
                });
                span.replaceWith(...spanContent);
            }
        }
        return newDiv.innerHTML;
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
        ckeditor.config.colorButton_colors = CKEDITOR.config.colorButton_colors;
        if (
            bloomEditableDiv.classList.contains(
                "bloom-copyFromOtherLanguageIfNecessary"
            ) &&
            bloomEditableDiv.innerText &&
            bloomEditableDiv.innerText !== "\n" &&
            bloomEditableDiv.getAttribute("data-user-deleted") !== "true"
        ) {
            // See BL-13779. If the user deletes all the text in a div, we don't want to copy
            // the text from another language in the future. We set the data-user-deleted attribute
            // to keep this from happening.
            ckeditor.on("change", event => {
                if (
                    bloomEditableDiv.innerText === "" ||
                    bloomEditableDiv.innerText === "\n"
                ) {
                    bloomEditableDiv.setAttribute("data-user-deleted", "true");
                }
            });
        }
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
            event.data.dataValue = this.restoreHtmlMarkupIfNecessary(
                event.data
            );
            event.data.dataValue = this.reconstituteParagraphsOnPlainTextPaste(
                event.data
            );
            event.data.dataValue = this.convertStandardFormatVerseMarkersToSuperscript(
                event.data.dataValue
            );

            event.data.dataValue = this.fixPasteData(event.data.dataValue);
            event.data.dataValue = this.removeUselessSpanMarkup(
                event.data.dataValue
            );

            // We can't just duplicate audio ids without running into trouble later!
            if (
                event.sender.element.$.getAttribute(
                    "data-audiorecordingmode"
                ) === "Sentence"
            ) {
                // We need to generate a new guid-based id, use it to copy the audio file, and
                // insert it into the span when targeting a Sentence recording mode div.
                event.data.dataValue = this.copyAudioFilesWithNewIdsDuringPasting(
                    event.data.dataValue
                );
            } else {
                // Remove all audio related span markup when targeting a TextBox recording mode div.
                event.data.dataValue = this.removeAudioSpanMarkupDuringPasting(
                    event.data.dataValue
                );
            }

            // OK, we are going to unwrap the first <p> and just leave its content.
            // This prevents a typically unwanted extra line break being inserted before the
            // start of the material copied. Without the <p> wrapper, that material just
            // gets inserted into the current paragraph.
            let endMkr = "";
            if (event.data.dataValue.match(/<p>/g)?.length === 1) {
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

        // The focus and blur event handlers ensure that the qtip tooltip for the
        // currently focused editor element is on top of any other qtip tooltips
        // such as Source Bubbles.
        // These tooltips are not to be confused with Source Bubbles.  The editor
        // element here is a single div.bloom-editable where the focus applies, possibly
        // enclosed by a div.bloom-translationGroup.  A Source Bubble qtip is attached
        // to the parent div.bloom-translationGroup if it exists.
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-11745.
        // NOTE:
        // * qtipOverrides.less handles the z-index for active-tooltip and styling for
        //   passive-bubble tooltips
        // * sourceBubbles.less handles the z-index for passive-bubble for Source Bubbles
        //   (for other tooltips, this defaults to what qtip places on the element itself)
        // * BloomSourceBubbles.SetupTooltips() has focus and blur handlers for Source Bubbles
        //   which remove or add the passive-bubble class.
        ckeditor.on("focus", event => {
            const qtipId = event.editor?.element?.getAttribute(
                "aria-describedby"
            );
            if (qtipId) {
                const tipElement = $(`#${qtipId}`);
                if (tipElement) {
                    tipElement.removeClass("passive-bubble");
                    tipElement.addClass("active-tooltip");
                }
            }
        });
        ckeditor.on("blur", event => {
            const qtipId = event.editor?.element?.getAttribute(
                "aria-describedby"
            );
            if (qtipId) {
                const tipElement = $(`#${qtipId}`);
                if (tipElement) {
                    tipElement.removeClass("active-tooltip");
                    tipElement.addClass("passive-bubble");
                }
            }
        });

        ckeditor.addCommand("pasteHyperlink", {
            exec: function(edt) {
                get("common/clipboardText", result => {
                    if (!result.data) {
                        return; // More sanity checks are in bloomEditing.updateCkEditorButtonStatus
                    }
                    const anchor = document.createElement("a");
                    anchor.href = result.data;
                    try {
                        document
                            .getSelection()
                            ?.getRangeAt(0)
                            ?.surroundContents(anchor);
                    } catch (ex) {
                        const englishErrorMessage =
                            "Bloom was not able to make a link. Try selecting only simple text.";
                        console.log(`${englishErrorMessage} ${ex}`);
                        BloomMessageBoxSupport.CreateAndShowSimpleMessageBox(
                            "EditTab.HyperlinkPasteFailure",
                            englishErrorMessage,
                            "Shows when a hyperlink cannot be pasted due to invalid selection.",
                            "CantPasteHyperlink"
                        );
                    }
                });
                return true; // probaby means success, but I'm not sure. Typescript says this function has to return a boolean.
            }
        });

        ckeditor.ui.addButton("PasteLink", {
            // add new button and bind our command
            label: "Paste Hyperlink",
            command: "pasteHyperlink",
            toolbar: "insert",
            icon: "/bloom/images/link.png"
        });

        // This makes it easy to find the right editor instance. There may be some ckeditor built-in way, but
        // I wasn't able to find one.
        (<any>bloomEditableDiv).bloomCkEditor = ckeditor;
    }

    // Spans are dropped from the clipboard's default data (eventData.dataValue) when the
    // source is CKEditor (ie, inside Bloom), but we need to keep them for possible color
    // markup.  See BL-12357.
    static restoreHtmlMarkupIfNecessary(eventData: any): any {
        const ckeId = eventData.dataTransfer.getData("cke/id");
        if (!ckeId) return eventData.dataValue; // live with the default if not from CKEditor
        const type = eventData.type;
        if (type !== "html") return eventData.dataValue; // live with the default if not HTML paste
        const fullHtml = eventData.dataTransfer.getData("text/html") as string;
        if (!fullHtml) return eventData.dataValue; // live with the default if no HTML (shouldn't happen by this point)
        const reducedHtml = eventData.dataValue as string;
        if (!reducedHtml) return eventData.dataValue; // live with the default if nothing to paste (shouldn't happen)
        if (fullHtml !== reducedHtml && fullHtml.includes("<span style=")) {
            const startMarker = "<!--StartFragment-->";
            const endMarker = "<!--EndFragment-->";
            if (
                reducedHtml.startsWith(startMarker) &&
                reducedHtml.endsWith(endMarker)
            ) {
                const start = fullHtml.indexOf(startMarker);
                const end = fullHtml.indexOf(endMarker) + endMarker.length;
                if (
                    start >= 0 &&
                    end >= start + startMarker.length + endMarker.length
                ) {
                    // actual fragment is marked with start and end comment markers
                    return fullHtml.substring(start, end);
                }
            } else {
                // actual fragment is the whole thing
                return fullHtml;
            }
        }
        return eventData.dataValue;
    }

    // If the original clipboard had no paragraph markup, but only (plain text) newlines (\n)
    // (e.g. pasting from Notepad), this method will remove the newlines and substitue HTML paragraph
    // markup.
    // The problem is that when pasting from Notepad, 'dataValue' has removed the newlines and squished
    // all the lines together into one line. Pasting the same thing from MSWord, 'dataValue' contains
    // HTML paragraph markup. So if there are no paragraph marks and there were (in the original
    // clipboard data) newlines, we reconstitute the needed paragraph marks from the original newlines
    // that we have to go fishing inside of the dataTransfer object to find. (BL-9961)
    static reconstituteParagraphsOnPlainTextPaste(eventData: any): any {
        if (eventData.type != "html") {
            // If we're inserting plain text from Notepad, we will arrive here because 'dataValue'
            // doesn't have paragraphs, but we actually do want paragraph markup, if there
            // are multiple lines in the original clipboard data.
            // Finding where the original string with its newlines was passed inside the
            // dataTransfer object was a bit tricky.
            const textWithReturns = eventData.dataTransfer.getData(
                "text/plain"
            ) as string;
            if (!textWithReturns.includes("\n")) {
                return eventData.dataValue; // no change
            }
            // Split the text on carriage returns and put it back together with each bit in a paragraph.
            const reconstitutedTextWithParas = textWithReturns
                .split("\n")
                .reduce(
                    (resultSoFar, part) => resultSoFar + "<p>" + part + "</p>",
                    ""
                );
            // Reset dataValue
            return reconstitutedTextWithParas;
        }
        return eventData.dataValue; // no change
    }

    // Not private so we can unit test it. It is too difficult to get the actual paste
    // event to get fired and handled correctly in tests.
    public static convertStandardFormatVerseMarkersToSuperscript(
        inputText: any
    ): any {
        const re = /\\v\s(\d+)/g;
        const matches = re.exec(inputText);
        if (matches == null) {
            //just let it paste
            return inputText;
        } else {
            // Use <sup> because that is what ckeditor uses
            return inputText.replace(re, "<sup>$1</sup>");
        }
    }

    // If copying one or more .audio-sentence spans, replace the ids and copy the original audio
    // file(s) using the new ids.
    // But if copying one or more .bloom-highlightSegment spans, remove the span markup.  (Note
    // that .audio-sentence and .bloom-highlightSegment classes are mutually exclusive since the
    // former is used for recording by sentence and the latter is used for recording by textbox.)
    public static copyAudioFilesWithNewIdsDuringPasting(
        inputHtml: string // could be plain text or have embedded/surrounding HTML markup
    ): string {
        const temp = document.createElement("template");
        temp.innerHTML = inputHtml;
        const nodelist = temp.content.querySelectorAll("span.audio-sentence");
        if (nodelist.length) {
            nodelist.forEach(
                (span: Element, key: number, parent: NodeListOf<Element>) => {
                    const oldId = span.getAttribute("id");
                    const newId = AudioRecording.createValidXhtmlUniqueId();
                    span.setAttribute("id", newId);
                    post(`audio/copyAudioFile?oldId=${oldId}&newId=${newId}`);
                }
            );
            return temp.innerHTML;
        }
        // span.audio-sentence doesn't exist, but we may have span.bloom-highlightSegment markup to remove.
        return this.removeMatchingAudioSpanMarkup(
            inputHtml,
            "span.bloom-highlightSegment"
        );
    }

    private static removeMatchingAudioSpanMarkup(
        inputHtml: string, // could be plain text or have embedded/surrounding HTML markup
        selector: string
    ): string {
        const temp = document.createElement("template");
        temp.innerHTML = inputHtml;
        const nodelist = temp.content.querySelectorAll(selector);
        if (nodelist.length) {
            let outputHtml = inputHtml;
            nodelist.forEach(
                (span: Element, key: number, parent: NodeListOf<Element>) => {
                    outputHtml = outputHtml.replace(
                        span.outerHTML,
                        span.innerHTML
                    );
                }
            );
            return outputHtml; // could be plain text by now even if input had HTML audio span markup
        }
        return inputHtml;
    }

    public static removeAudioSpanMarkupDuringPasting(
        inputHtml: string // could be plain text or have embedded/surrounding HTML markup
    ): string {
        return this.removeMatchingAudioSpanMarkup(
            inputHtml,
            "span.audio-sentence, span.bloom-highlightSegment"
        );
    }

    private static MakeShiftEnterInsertLineBreak(field: HTMLElement) {
        $(field).keypress(e => {
            //NB: This will not fire in the (now normal case) that ckeditor is in charge of this field.
            if (e.key === "Enter") {
                if (e.shiftKey) {
                    BloomField.InsertLineBreak();
                    e.stopPropagation();
                    e.preventDefault();
                }
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
    // work it into editTabBundle etc.
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

        const divToProtect = $(field).find(
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
            if (e.key == "Backspace") {
                const sel = window.getSelection();
                if (!sel || !sel.anchorNode) return;
                //Are we at the start of a paragraph with nothing selected?
                if (sel.anchorOffset == 0 && sel.isCollapsed) {
                    //Are we in the first paragraph?
                    //Embedded image divs come before the first editable paragraph, so we look at the previous element and
                    //see if it is one those. Anything marked with bloom-preventRemoval is probably not something we want to
                    //be merging with.
                    const previousElement = $(sel.anchorNode)
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
            const leftArrowPressed = e.key === "ArrowLeft";
            const rightArrowPressed = e.key === "ArrowRight";
            if (leftArrowPressed || rightArrowPressed) {
                const sel = window.getSelection();
                if (sel && sel.anchorNode === this) {
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
        const nodes = field.childNodes;
        for (let n = 0; n < nodes.length; n++) {
            const node = nodes[n];
            if (node.nodeType === 3) {
                //Node.TEXT_NODE
                const paragraph = document.createElement("p");
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
    private static EnsureParagraphsPresent(field: HTMLElement) {
        // Allow designers freedom to work without paragraphs
        if ($(field).hasClass("bloom-noParagraphs")) return;
        // The Wordfind page for the Story Primer template was written assuming no paragraphs for the grid cells.
        // Inserting paragraphs into the cells breaks the grid.  See BL-7061.
        if ($(field).hasClass("WordFind-style")) return;

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
        const range = document.createRange();
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
        const sel = window.getSelection();
        if (sel) {
            sel.removeAllRanges();
            sel.addRange(range);
        }
    }

    private static ManageWhatHappensIfTheyDeleteEverything(field: HTMLElement) {
        // if the user types (ctrl+a, del) then we get an empty element or '<br></br>', and need to get a <p> in there.
        // if the user types (ctrl+a, 'blah'), then we get blah outside of any paragraph

        $(field).keyup(e => {
            if ($(field).find("p").length === 0) {
                BloomField.EnsureParagraphsPresent(field);

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
        const numberThatShouldBeThere = $(field).find(".bloom-preventRemoval")
            .length;
        if (numberThatShouldBeThere > 0) {
            $(field).keyup(e => {
                if (
                    $(field).find(".bloom-preventRemoval").length <
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
        const field = event.target;
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

            const selection: FFSelection = window.getSelection() as FFSelection;

            //if we're at the start of the text, we're to the left of the character we want to replace
            if (selection.anchorOffset === 0) {
                let doNotDeleteOrMove = false;
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
        field.addEventListener("keydown", (e: KeyboardEvent) => {
            if (e.key === "Backspace") {
                // We want to prevent backspace if we're at the very start of the 1st paragraph...
                // But we need to make sure we properly handle the cases where there are child elements in it.
                // (e.g. sentence spans generated by Talking Book tool)
                const sel = window.getSelection();
                if (!sel || !sel.anchorNode) return;

                //If we're not at the start of something with nothing selected, we can return.
                //FYI: anchorOffset means different things depending on if anchorNode is a text node or an element,
                //     but in either case, we don't need to prevent backspace if anchorOffset isn't 0.
                if (!(sel.anchorOffset === 0 && sel.isCollapsed)) {
                    return;
                }

                //Are we in the first paragraph?
                //FYI: Nested paragraphs don't seem to be allowed. So don't need to worry about that possibility.
                const closestP = $(sel.anchorNode).closest("P");
                const previousElement = closestP.prev();

                if (previousElement.length !== 0) {
                    // not in the 1st paragraph. Nothing to worry about. Return.
                    return;
                }

                //If anchorNode is some kind of child node of closestP
                if (closestP.length > 0 && closestP[0] !== sel.anchorNode) {
                    //If it's not the 1st child, then there's nothing to worry about.
                    if (
                        !this.isNodeALeftMostDescendant(
                            closestP[0],
                            sel.anchorNode
                        )
                    ) {
                        return;
                    }
                }

                //At this point, looks like it is something to worry about.
                //Need to prevent the backspace from happening.
                e.stopPropagation();
                e.preventDefault();
                console.log("Prevented Backspace");
            }
        });
    }

    // Checks if targetElement is on the path to the 1st leaf node of rootElement.
    // That is, it's on the "left"-most path if you drew it as a tree structure with the earlier children on the left.
    private static isNodeALeftMostDescendant(
        rootElement: Element,
        targetNode: Node
    ) {
        let curr = rootElement.firstChild;
        while (curr) {
            if (targetNode === curr) {
                return true;
            }

            curr = curr.firstChild;
        }

        // curr is now null
        return false;
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
