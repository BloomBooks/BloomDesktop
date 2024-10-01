/*
 *  Project: Long Press
 *  Description: Pops a list of alternate characters when a key is long-pressed
 *  Author: Quentin Thiaucourt, http://toki-woki.net
 *    Licence: MIT License http://opensource.org/licenses/mit-license.php
 *
 *  Modified Oct 2014 to work with editable divs also
 *  Modified August 2015 to remove arrow key feature, which was interfering with ckeditor (or vice-versa, really)
 *  Modified August 2015 to add instructions at the bottom
 *  Modified September 2015 to set focus before selection in restoreCaretPosition()
 */
import { EditableDivUtils } from "../../bookEdit/js/editableDivUtils";
import { isLongPressEvaluating } from "../../bookEdit/toolbox/toolbox";
require("./jquery.mousewheel.js");

(function($, window, undefined) {
    var lengthOfPreviouslyInsertedCharacter = 1; //when we convert to Typescript, this is a member variable

    var pluginName = "longPress",
        document = window.document,
        defaults = {
            instructions: ""
        };

    // See https://issues.bloomlibrary.org/youtrack/issue/BL-8683.
    // We originally set out to include the normal no-break space (\u00A0),
    // but browsers consistently convert this into a space if you type anything before or after them.
    const narrowNoBreakSpace = "\u202F";
    const nonBreakingHyphen = "\u2011";
    const enDash = "\u2013";
    const emDash = "\u2014";

    const characterSets = splitCharacterSetsByGrapheme({
        // extended latin (and african latin)
        // upper
        A: "ĀĂÀÁÂÃÄÅĄẠA̱ⱭÆ∀",
        B: "Ɓ",
        C: "ÇĆĈƆ̃ĊČƆƇ",
        D: "ÐĎĐḎƊ",
        E: "ÈÉÊẼËĒĖĘẸĚE̱ƏÆƎƐ€",
        F: "ƑƩ",
        G: "ĜĞĠǴĢƢ",
        H: "ĤĦ",
        I: "ÌÍȊĬÎǏÏḮĨȈĮĪỈỊḬI̱ƗİĲ",
        J: "ĴĲ",
        K: "ĶƘ",
        L: "ĹĻĽŁΛ",
        N: "ÑŃŅŇŊƝ₦",
        O: "ÒÓÔÕÖŌŐỌO̱ØŒƠƟƆƆ̃◌",
        P: "Ƥ¶",
        R: "ŔŘɌⱤ",
        S: "ßſŚŜŞṢŠÞ§",
        T: "ŢŤṮƬƮ",
        U: "ÙÚÛŨÜŪŬŮŰŲỤU̱ɄƯƱ",
        V: "Ʋ",
        W: "ŴẄΩ",
        X: "Ẍ",
        Y: "ÝŶŸƔƳɎ",
        Z: "ŹŻŽƵƷẔ",

        // lower
        a: "āăàáâãäåąạa̱ɑæαª",
        b: "ßβɓ",
        c: "çςćĉċčɔ̃ƈ¢ɔ©",
        d: "ðďđɖḏɖɗ",
        e: "èéêẽëēėęẹěe̱əæεɛ€",
        f: "ƒʃƭ",
        g: "ĝğġģǵɠƣ",
        h: "ĥħɦẖ",
        i: "ìíȋĭîǐïḯĩȉįīỉịḭi̱ɨıĳɪᵻᶖι",
        j: "ĵɟʄĳ",
        k: "ķƙ",
        l: "ĺļľłλ",
        n: "ñńņňŋɲ",
        o: "òóôõöōọo̱øőœơɵ°ɔɔ̃◌",
        p: "ƥ¶",
        r: "ŕřɍɽ",
        s: "ßſśŝşṣšþ§",
        t: "ţťṯƭʈ",
        u: "ùúûũüūŭůűųưμυụu̱ʉʊ",
        v: "ʋ",
        w: "ŵẅω",
        x: "ẍ",
        y: "ýŷÿɣƴɏ",
        z: "źżžƶẕʒƹ",

        // Numbers
        0: "⁰₀",
        1: "¹₁",
        2: "²₂",
        3: "³₃",
        4: "⁴₄",
        5: "⁵₅",
        6: "⁶₆",
        7: "⁷₇",
        8: "⁸₈",
        9: "⁹₉",

        // Misc
        $: "£¥€₩₨₳Ƀ¤",
        "!": "¡‼‽",
        "?": "¿‽",
        "%": "‰",
        ".": "…•",
        "-": nonBreakingHyphen + enDash + emDash + "±",
        "+": "±†‡",
        "\\": "′″‴‘’‚‛",
        "'": "ꞌʼ",
        '"': "“”„‟Ꞌ",
        "<": "«≤‹",
        ">": "»≥›",
        "=": "≈≠≡",
        "/": "÷",
        "\u0020": narrowNoBreakSpace, // Spacebar; see comment on narrowNoBreakSpace above
        "\u00a0": narrowNoBreakSpace // Spacebar (see BL-12191); see comment on narrowNoBreakSpace above
    });
    // http://www.cambiaresearch.com/articles/15/javascript-char-codes-key-codes
    // 8  backspace
    // 9  tab
    // 13 enter
    // 16 shift (by itself; it still works to hold down capital letters)
    // 17 ctrl
    // 18 alt
    // 27 escape
    // 33 page up
    // 34 page down
    // 35 end
    // 36 home
    // 37 left arrow
    // 38 up arrow
    // 39 right arrow
    // 40 down arrow
    // 45 insert
    // 46 delete
    // Review: there are others we could add, function keys, num lock, scroll lock, break, forward & back slash, etc.
    //  not sure how much we gain from that...
    var ignoredKeyDownKeyCodes = [
        8,
        9,
        13,
        16,
        17,
        18,
        27,
        33,
        34,
        35,
        36,
        37,
        38,
        39,
        40,
        45,
        46
    ];
    var ignoredKeyUpKeys = [
        8,
        9,
        13,
        /*16,*/ 17,
        18,
        27,
        33,
        34,
        35,
        36,
        37,
        38,
        39,
        40,
        45,
        46
    ];

    var selectedCharIndex;
    var activationKey;
    var timer;
    var activeElement;
    var textAreaCaretPosition;
    var storedOffset;
    var shortcuts = [];
    var popup;
    var longpressPopupVisible = false;

    $(window).mousewheel(onWheel);

    //https://stackoverflow.com/questions/10758913/unicode-string-split-by-chars/39846360#39846360
    function splitIntoGraphemes(s) {
        const re = /.[\u0300-\u036F]*/g; // notice, currently only handles the Combining Diacritical Marks range
        var match,
            matches = [];
        while ((match = re.exec(s))) matches.push(match[0]);
        return matches;
    }

    // chop up the lists like
    //   'a': 'āa̱ɑ'
    // into an array that puts the combining characters together, like
    //     'a': [ā, a_, ɑ]  (the underline there represents the "combining macron below" (U+0331))
    function splitCharacterSetsByGrapheme(sets) {
        var output = {};
        Object.keys(sets).forEach(key => {
            output[key] = splitIntoGraphemes(sets[key]);
        });
        return output;
    }

    function makeShortcuts(skipKey) {
        shortcuts = [];
        //while numbers are the most convenient, we are not using them because
        //when the user is trying to get a capital letter, the shift key is held
        //down and numbers are converted to symbols. I don' know of a way to convert
        //those symbols back to numbers in a way that works across different keyboards
        // for(var i = 1; i < 10;i++){
        //     shortcuts.push(String.fromCharCode(48+i));//48 is '1';
        // }
        for (var i = 0; i < 26; i++) {
            //the character used to invoke longPress can't be pressed again as a shortcut,
            //same for the shifted version of the character.
            var key = String.fromCharCode(97 + i);
            if (key != skipKey && key != skipKey.toLowerCase()) {
                //we use uppercase because that's what you see on the keys of the physical keyboard
                shortcuts.push(key.toUpperCase()); //97 is charcode for 'a';
            }
        }
    }

    function onKeyDown(e) {
        // See comment for BL-5215 in toolbox.ts
        window.top[isLongPressEvaluating] = true;

        /* we had to disable thes because ckeditor was seeing them and messing things up. Hopefully in the future it can be reinstated:
        // Arrow key with popup visible
        if ($('.long-press-popup').length > 0 && (e.which == 37 || e.which == 39)) {
                e.stopPropagation(); //stop ckeditor from seeing this (DOESN"T WORK)
                e.preventDefault();
                if (e.which == 37) activePreviousLetter();
                else if (e.which==39) activateNextLetter();


                return;
        }
        */

        //once the panel is showing, let the user type any of the shortcuts to select the corresponding character
        if (longpressPopupVisible && activationKey != e.key) {
            var unshiftedKey = e.key.toUpperCase();
            var indexOfSelectedCharacter = shortcuts.indexOf(unshiftedKey);
            if (indexOfSelectedCharacter >= 0) {
                e.preventDefault();
                e.stopPropagation();
                selectCharIndex(indexOfSelectedCharacter);
                return;
            }
        }

        if (ignoredKeyDownKeyCodes.indexOf(e.which) > -1) return;
        activeElement = e.target;
        if (e.key == activationKey) {
            e.preventDefault();
            e.stopPropagation(); //attempt to stop ckeditor from seeing this event
            makeShortcuts(activationKey);
            if (!timer) timer = setTimeout(onTimer, 10);
            return;
        }
        activationKey = e.key;
    }

    function onKeyUp(e) {
        try {
            if (ignoredKeyUpKeys.indexOf(e.which) > -1) return;
            if (activeElement == null) return;

            activationKey = null;
            clearTimeout(timer);
            timer = null;

            hidePopup();
        } finally {
            window.top[isLongPressEvaluating] = false;
        }
    }
    function onTimer() {
        var typedChar = isTextArea()
            ? $(activeElement)
                  .val()
                  .split("")[getTextAreaCaretPosition(activeElement) - 1]
            : $(activeElement)
                  .text()
                  .split("")[getCaretPositionOffset(activeElement) - 1];

        if (characterSets[typedChar]) {
            storeCaretPosition();
            showPopup(characterSets[typedChar]);
        } else {
            hidePopup();
        }
    }
    const charactersRepresentedByAlternativeText = {
        [narrowNoBreakSpace]:
            "narrow non-breaking space / espace fine insécable",
        [nonBreakingHyphen]:
            "- (non-breaking hyphen / trait d'union insécable)",
        [enDash]: "– (en dash / tiret demi-cadratin)",
        [emDash]: "— (em dash / tiret cadratin)"
    };
    function createOneButton(replacementText, shortcutText) {
        let cssClass = "long-press-letter";
        let buttonText = replacementText;

        if (
            Object.keys(charactersRepresentedByAlternativeText).includes(
                replacementText
            )
        ) {
            cssClass += " small-text";
            buttonText =
                charactersRepresentedByAlternativeText[replacementText];
        }

        const letter = $(
            `<li class="${cssClass}" data-value=${replacementText} data-shortcut="${shortcutText}">${buttonText}</li>`
        );
        letter.mouseenter(activateLetter);
        letter.click(onPopupLetterClick);
        popup.find("ul").append(letter);
    }
    function showPopup(chars) {
        lengthOfPreviouslyInsertedCharacter = 1; //the key they are holding down will be a single character
        popup.find("ul").empty();
        for (var i = 0; i < chars.length; i++) {
            createOneButton(chars[i], shortcuts[i]);
        }

        //When the parent body is scaled, we don't want our popup to scale
        var bodyScale =
            document.body.getBoundingClientRect().width /
            document.body.offsetWidth;
        var compensationScale = 1.0 / bodyScale;

        // for now, a good test case is 1024px wide bloom window, and hold down 'i'
        // Height is automatic and vertical position is locked to the bottom of the window.
        //limit to the visible width that we can use
        var visibleWidth =
            window.innerWidth ||
            document.documentElement.clientWidth ||
            document.body.clientWidth ||
            0;
        visibleWidth = visibleWidth - 25; // fudge
        popup.css("width", visibleWidth + "px");

        //reverse the scaling that we get from parent
        popup.css("transform", "scale(" + compensationScale + ")");
        popup.css("transform-origin", "top left");

        $("body").append(popup);
        selectedCharIndex = -1;
        longpressPopupVisible = true;
    }
    function onPopupLetterClick(e) {
        restoreCaretPosition();
        hidePopup();
    }
    function activateLetter(e) {
        selectCharIndex($(e.target).index());
    }
    function activateRelativeLetter(i) {
        selectCharIndex(
            ($(".long-press-letter").length + selectedCharIndex + i) %
                $(".long-press-letter").length
        );
    }
    function activateNextLetter() {
        activateRelativeLetter(1);
    }
    function activePreviousLetter() {
        activateRelativeLetter(-1);
    }
    function hidePopup() {
        longpressPopupVisible = false;
        popup.detach();
    }
    function onWheel(e, delta, deltaX, deltaY) {
        if ($(".long-press-popup").length == 0) return;
        e.preventDefault();
        delta < 0 ? activateNextLetter() : activePreviousLetter();
    }
    function selectCharIndex(i) {
        $(".long-press-letter.selected").removeClass("selected");
        $(".long-press-letter")
            .eq(i)
            .addClass("selected");
        selectedCharIndex = i;
        updateChar();
    }

    function updateChar() {
        const newChar = $(".long-press-letter.selected").attr("data-value");
        if (newChar !== undefined) {
            replacePreviousLetterWithNewLetter(newChar);
        }
    }

    function isTextArea() {
        return $(activeElement).is("textarea");
    }

    function storeCaretPosition() {
        if (isTextArea()) {
            textAreaCaretPosition = getTextAreaCaretPosition(activeElement);
        } else {
            storedOffset = EditableDivUtils.getElementSelectionIndex(
                activeElement
            );
        }
    }

    function restoreCaretPosition() {
        if (isTextArea()) {
            setTextAreaCaretPosition(activeElement, textAreaCaretPosition);
        } else {
            // If we make the selection before setting the focus, the selection
            // ends up in the wrong place (BL-2717).
            if (activeElement && typeof activeElement.focus !== "undefined") {
                // When the IP is at the start of a paragraph, sometimes there is a side effect of setting focus
                // that inserts a zero-width space at the beginning of the paragraph. It probably has something
                // to do with CkEditor bookmarks, but I have not been able to track it down
                // beyond the fact that it happens exactly during this setfocus() call.
                // It causes an immediate problem because it makes the offset we've remembered wrong.
                // We could adjust the focus, but we don't want these extra zwsp's in the text anyway.
                // And there is no legitimate reason for an extra one to appear between the keydown for
                // a longpress and a mouse click. So we just remove them.
                // This is a horrible kludge, but hopefully we can retire it when we retire CkEditor.
                // Note: I tried setting the focus after restoring the selection, but the selection ends
                // up in the wrong place and we still sometimes get spurious zwsp's. I don't fully
                // understand what is happening. I suspect CkEditor has its own idea of where the selection
                // should be and is moving it when its element gets focus. But I don't know how to stop it.
                // Similar things happen if I don't set the focus here at all...guessing that focus
                // automatically returns from the button to the edit box and the guilty focus handler still runs.
                // Another possible approach, which I have not tried, would be to prevent
                // the longpress buttons ever getting focus (using preventDefault on mousedown).
                // The only reason for them to have it would
                // be for keyboard accessibility, but we already have a way to select the character we
                // want from the keyboard.
                activeElement.focus();
                // loop until we don't find a spurious zwsp, see comments below
                for (;;) {
                    EditableDivUtils.makeSelectionIn(
                        activeElement,
                        storedOffset,
                        -1, // no brs around that we need to skip
                        // If we're at a paragraph boundary, we are at the end of the previous paragraph.
                        // (We just inserted a character, so we can't possibly be at the start of a paragraph.
                        // It's possible that we're at a boundary between two text nodes, especially since
                        // longpress likes to insert the text as an extra node, but in that case it doesn't
                        // matter which one we make the selection in.)
                        false
                    );
                    // Check for a problem where something, probably CkEditor, inserts a zero-width space before the
                    // special character we just inserted.
                    const selection = window.getSelection();
                    const range = selection.getRangeAt(0).cloneRange();
                    range.setStart(range.startContainer, range.startOffset - 1);
                    // Is the character before the caret a zero-width space?
                    if (range.toString() !== "\u200B") {
                        // It's not; we no longer have the problem (or never did)
                        break;
                    }
                    // We definitely don't expect that the character we just inserted is a zwsp.
                    // If that's what we find right before the restored selection, we remove it.
                    // (This will have to be fixed some other way if we ever want to support inserting
                    // zwsp's using longpress. Hopefully by then we've replaced CkEditor and can retire this fix.)
                    range.deleteContents();
                    // The caret was left BEFORE the character we intended to insert by the makeSelectionIn: call
                    // above, because of the unexpected zwsp. So loop around and make the selection again.
                    // I'm not sure it ever happens that we find yet another zwsp, though I think it happened
                    // once. In any case, we don't want one where we inserted something else, so if we do
                    // find more, we'll keep deleting them.
                }
            }
        }
    }

    function setFocusDelayed() {
        window.setTimeout(function() {
            if (activeElement && typeof activeElement.focus != "undefined") {
                activeElement.focus();
            }
        }, 1);
    }

    // See notes on BL-3900 in toolbox.ts for important regression information.
    function replacePreviousLetterWithNewLetter(newLetter) {
        if (isTextArea()) {
            const pos = getTextAreaCaretPosition(activeElement);
            const arVal = $(activeElement)
                .val()
                .split("");
            arVal[pos - 1] = newLetter;
            $(activeElement).val(arVal.join(""));
            setTextAreaCaretPosition(activeElement, pos);
        } else {
            const insertPointRange = getCaretPosition();
            if (
                window.getSelection &&
                insertPointRange &&
                insertPointRange.startOffset != 0
            ) {
                const sel = window.getSelection();
                if (sel.getRangeAt && sel.rangeCount) {
                    //NB: From BL-3900 investigation:
                    //if the startContainer is a span, then it has an internal text node, and so deleting
                    //span:0-->1 takes out the entire text node, not just one character.
                    //My hypothesis is that the markup code was leaving the selection a bit messed up.
                    //That code has been changed, so this should not happen, but if it does, this will save
                    //some debugging time.
                    if (insertPointRange.startContainer.nodeName != "#text") {
                        throw "longpress: aborting becuase deleteContents() would have deleted all contents of a " +
                            insertPointRange.startContainer.nodeName;
                    }

                    //remove the character they typed to open this tool
                    const rangeToRemoveStarterCharacter = insertPointRange.cloneRange();
                    rangeToRemoveStarterCharacter.setStart(
                        insertPointRange.startContainer,
                        insertPointRange.startOffset -
                            lengthOfPreviouslyInsertedCharacter
                    );
                    rangeToRemoveStarterCharacter.setEnd(
                        insertPointRange.startContainer,
                        insertPointRange.startOffset
                    );
                    rangeToRemoveStarterCharacter.deleteContents();

                    //stick in the replacement character
                    const textNode = document.createTextNode(newLetter);
                    insertPointRange.insertNode(textNode);

                    //composed characters can be more than one unicode value, e.g. a̱
                    //so remember what we inserted so that if we go to another character before releasing, using mouse or mouseweel, we can remove this one
                    lengthOfPreviouslyInsertedCharacter = newLetter.length;

                    // Move caret to the end of the newly inserted text node
                    insertPointRange.setStart(textNode, textNode.length);
                    insertPointRange.setEnd(textNode, textNode.length);
                    sel.removeAllRanges();
                    sel.addRange(insertPointRange);
                }
            }
        }
    }

    function getCaretPosition() {
        var sel = window.getSelection();
        return sel.getRangeAt(0);
    }

    function getCaretPositionOffset(element) {
        var caretOffset = 0;
        var doc = element.ownerDocument || element.document;
        var win = doc.defaultView || doc.parentWindow;
        var sel;
        if (typeof win.getSelection != "undefined") {
            sel = win.getSelection();
            if (sel.rangeCount > 0) {
                var range = win.getSelection().getRangeAt(0);
                var preCaretRange = range.cloneRange();
                preCaretRange.selectNodeContents(element);
                preCaretRange.setEnd(range.endContainer, range.endOffset);
                caretOffset = preCaretRange.toString().length;
            }
        } else if ((sel = doc.selection) && sel.type != "Control") {
            var textRange = sel.createRange();
            var preCaretTextRange = doc.body.createTextRange();
            preCaretTextRange.moveToElementText(element);
            preCaretTextRange.setEndPoint("EndToEnd", textRange);
            caretOffset = preCaretTextRange.text.length;
        }
        return caretOffset;
    }

    function getTextAreaCaretPosition(ctrl) {
        var caretPos = 0;
        if (document.selection) {
            // IE Support
            ctrl.focus();
            var sel = document.selection.createRange();
            sel.moveStart("character", -ctrl.value.length);
            caretPos = sel.text.length;
        } else if (ctrl.selectionStart || ctrl.selectionStart == "0") {
            // Firefox support
            caretPos = ctrl.selectionStart;
        }
        return caretPos;
    }
    function setTextAreaCaretPosition(ctrl, pos) {
        if (ctrl.setSelectionRange) {
            ctrl.focus();
            ctrl.setSelectionRange(pos, pos);
        } else if (ctrl.createTextRange) {
            var range = ctrl.createTextRange();
            range.collapse(true);
            range.moveEnd("character", pos);
            range.moveStart("character", pos);
            range.select();
        }
    }

    function LongPress(element, options) {
        this.element = element;
        this.options = $.extend({}, defaults, options);

        this._defaults = defaults;
        this._name = pluginName;

        //popup = $('<div id="longpress" class="long-press-popup"><ul />' + this.options.instructions + '</div>');

        popup = window.top.$(
            '<div id="longpress" class="long-press-popup"><ul />' +
                this.options.instructions +
                "</div>"
        );
        this.init();
    }

    LongPress.prototype = {
        init: function() {
            $(this.element).keydown(onKeyDown);
            $(this.element).keyup(onKeyUp);
        }
    };

    $.fn[pluginName] = function(options) {
        return this.each(function() {
            if (!$.data(this, "plugin_" + pluginName)) {
                $.data(
                    this,
                    "plugin_" + pluginName,
                    new LongPress(this, options)
                );
            }
        });
    };
})(jQuery, window);
