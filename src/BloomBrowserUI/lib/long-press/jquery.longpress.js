/*
 *  Project: Long Press
 *  Description: Pops a list of alternate characters when a key is long-pressed
 *  Author: Quentin Thiaucourt, http://toki-woki.net
 *    Licence: MIT License http://opensource.org/licenses/mit-license.php
 *
 *  Modified Oct 2014 to work with editable divs also
 *  Modified August 2015 to remove arrow key feature, which was interfering with ckeditor (or vice-versa, really)
 */

;(function ($, window, undefined) {

    var pluginName = 'longPress',
        document = window.document,
        defaults = {/*
            propertyName: "value"
        */};

    var moreChars={
        // extended latin (and african latin)
        // upper
        'A':'ĀĂÀÁÂÃÄÅĄⱭ∀Æ',
        'B':'Ɓ',
        'C': 'ÇĆĈƆ̃ĊČƆ',
        'D':'ÐĎĐḎƊ',
        'E': 'ÈÉÊẼËĒĖĘẸĚƏÆƎƐ€',
        'F':'ƑƩ',
        'G':'ĜĞĠĢƢ',
        'H':'ĤĦ',
        'I':'ÌÍȊĬÎǏÏḮĨȈĮĪỈỊḬƗİĲ',
        'J':'ĴĲ',
        'K':'ĶƘ',
        'L':'ĹĻĽŁΛ',
        'N':'ÑŃŅŇŊƝ₦',
        'O':'ÒÓÔÕÖŌØŐŒƠƟ',
        'P':'Ƥ¶',
        'R':'ŔŘɌⱤ',
        'S':'ßſŚŜŞṢŠÞ§',
        'T':'ŢŤṮƬƮ',
        'U':'ÙÚÛŨÜŪŬŮŰŲɄƯƱ', 
        'V':'Ʋ',
        'W':'ŴẄΩ',
        'Y':'ÝŶŸƔƳ',
        'Z':'ŹŻŽƵƷẔ',

        // lower
        'a':'āăàáâãäåąɑæαª',
        'b':'ßβɓ',
        'c': 'çςćĉɔ̃ċč¢ɔ',
        'd':'ðďđɖḏɖɗ',
        'e':'èéêẽëēėęẹěəæεɛ€', 
        'f':'ƒʃƭ',
        'g':'ĝğġģɠƣ',
        'h':'ĥħɦẖ',
        'i':'ìíȋĭîǐïḯĩȉįīỉịḭɨıĳɪᵻᶖι',
        'j':'ĵɟʄĳ',
        'k':'ķƙ',
        'l':'ĺļľłλ',
        'n':'ñńņňŋɲ',
        'o':'òóôõöōøőœơɵ°',
        'p':'ƥ¶',
        'r':'ŕřɍɽ',
        's':'ßſśŝşṣšþ§',
        't':'ţťṯƭʈ',
        'u': 'ùúûũüūŭůűųưμυʉʊ',
        'v':'ʋ',
        'w':'ŵẅω',
        'y':'ýŷÿɣyƴ',
        'z':'źżžƶẕʒƹ',

        // Misc
        '$':'£¥€₩₨₳Ƀ¤',
        '!':'¡‼‽',
        '?':'¿‽',
        '%':'‰',
        '.':'…•',
        '-':'±‐–—',
        '+':'±†‡',
        '\\':'′″‴‘’‚‛',
        '"':'“”„‟',
        '<':'≤‹',
        '>':'≥›',
        '=':'≈≠≡'

    };
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
    var ignoredKeyDownKeys=[8, 9, 13, 16, 17, 18, 27, 33, 34, 35, 36, 37, 38, 39, 40, 45, 46];
    var ignoredKeyUpKeys = [8, 9, 13, /*16,*/ 17, 18, 27, 33, 34, 35, 36, 37, 38, 39, 40, 45, 46];

    var selectedCharIndex;
    var lastWhich;
    var timer;
    var activeElement;
    var textAreaCaretPosition;
    var storedOffset;

    var popup=$('<ul class=long-press-popup />');

    $(window).mousewheel(onWheel);

    function onKeyDown(e) {
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

        if (ignoredKeyDownKeys.indexOf(e.which)>-1) return;
        activeElement=e.target;

        if (e.which==lastWhich) {
            e.preventDefault();
            if (!timer) timer=setTimeout(onTimer, 10);
            return;
        }
        lastWhich=e.which;
    }
    function onKeyUp(e) {
        if (ignoredKeyUpKeys.indexOf(e.which) > -1) return;
        if (activeElement == null) return;

        // allow them to hold down the shift key after pressing a letter, 
        // then use their other hand to do mouse or arrow keys.
        if (e.shiftKey) return;

        lastWhich=null;
        clearTimeout(timer);
        timer=null;

        hidePopup();
    }
    function onTimer() {
        var typedChar = isTextArea() ?
            $(activeElement).val().split('')[getTextAreaCaretPosition(activeElement)-1] :
            $(activeElement).text().split('')[getCaretPositionOffset(activeElement)-1];

        if (moreChars[typedChar]) {
            storeCaretPosition();
            showPopup((moreChars[typedChar]));
        } else {
            hidePopup();
        }
    }
    function showPopup(chars) {
        popup.empty();
        var letter;
        for (var i=0; i<chars.length; i++) {
            letter=$('<li class=long-press-letter />').text(chars[i]);
            letter.mouseenter(activateLetter);
            letter.click(onPopupLetterClick);
            popup.append(letter);
        }
        $('body').append(popup);
        selectedCharIndex=-1;
    }
    function onPopupLetterClick(e) {
        restoreCaretPosition();
        hidePopup();
    }
    function activateLetter(e) {
        selectCharIndex($(e.target).index());
    }
    function activateRelativeLetter(i) {
        selectCharIndex(($('.long-press-letter').length+selectedCharIndex+i) % $('.long-press-letter').length);
    }
    function activateNextLetter() {
        activateRelativeLetter(1);
    }
    function activePreviousLetter() {
        activateRelativeLetter(-1);
    }
    function hidePopup() {
        popup.detach();
    }
    function onWheel(e, delta, deltaX, deltaY) {
        if ($('.long-press-popup').length==0) return;
        e.preventDefault();
        delta<0 ? activateNextLetter() : activePreviousLetter();
    }
    function selectCharIndex(i) {
        $('.long-press-letter.selected').removeClass('selected');
        $('.long-press-letter').eq(i).addClass('selected');
        selectedCharIndex=i;
        updateChar();
    }

    function updateChar() {
        var newChar=$('.long-press-letter.selected').text();
        replacePreviousLetterWithText(newChar);
    }

    function isTextArea() {
        return $(activeElement).is('textarea');
    }

    function storeCaretPosition() {
        if (isTextArea()) {
            textAreaCaretPosition = getTextAreaCaretPosition(activeElement);
        } else {
            storedOffset = EditableDivUtils.getElementSelectionIndex(activeElement);
        }
    }

    function restoreCaretPosition() {
        if (isTextArea()) {
            setTextAreaCaretPosition(activeElement, textAreaCaretPosition);
        } else {
            EditableDivUtils.makeSelectionIn(activeElement, storedOffset, null, true);
            setFocusDelayed();
        }
    }

    function setFocusDelayed() {
        window.setTimeout(function() {
            if (activeElement && typeof activeElement.focus != "undefined") {
                activeElement.focus();
            }
        }, 1);
    }

    function replacePreviousLetterWithText(text) {
        if (isTextArea()) {
            var pos=getTextAreaCaretPosition(activeElement);
            var arVal=$(activeElement).val().split('');
            arVal[pos-1]=text;
            $(activeElement).val(arVal.join(''));
            setTextAreaCaretPosition(activeElement, pos);
        } else {
            var sel, textNode, clone;
            var range = getCaretPosition();
            if (window.getSelection && range && range.startOffset != 0) {
                sel = window.getSelection();
                if (sel.getRangeAt && sel.rangeCount) {
                    textNode = document.createTextNode(text);

                    clone = range.cloneRange();
                    clone.setStart(range.startContainer, range.startOffset - 1);
                    clone.setEnd(range.startContainer, range.startOffset);
                    clone.deleteContents();
                    range.insertNode(textNode);

                    // Move caret to the end of the newly inserted text node
                    range.setStart(textNode, textNode.length);
                    range.setEnd(textNode, textNode.length);

                    sel.removeAllRanges();
                    sel.addRange(range);
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
        } else if ( (sel = doc.selection) && sel.type != "Control") {
            var textRange = sel.createRange();
            var preCaretTextRange = doc.body.createTextRange();
            preCaretTextRange.moveToElementText(element);
            preCaretTextRange.setEndPoint("EndToEnd", textRange);
            caretOffset = preCaretTextRange.text.length;
        }
        return caretOffset;
    }

    function getTextAreaCaretPosition (ctrl) {
        var caretPos = 0;
        if (document.selection) {
            // IE Support
            ctrl.focus ();
            var sel = document.selection.createRange ();
            sel.moveStart ('character', -ctrl.value.length);
            caretPos = sel.text.length;
        } else if (ctrl.selectionStart || ctrl.selectionStart == '0') {
            // Firefox support
            caretPos = ctrl.selectionStart;
        }
        return caretPos;
    }
    function setTextAreaCaretPosition(ctrl, pos) {
        if (ctrl.setSelectionRange) {
            ctrl.focus();
            ctrl.setSelectionRange(pos,pos);
        } else if (ctrl.createTextRange) {
            var range = ctrl.createTextRange();
            range.collapse(true);
            range.moveEnd('character', pos);
            range.moveStart('character', pos);
            range.select();
        }
    }

    function LongPress( element, options ) {

        this.element = element;
        this.options = $.extend( {}, defaults, options) ;

        this._defaults = defaults;
        this._name = pluginName;

        this.init();
    }

    LongPress.prototype = {

        init: function () {
            $(this.element).keydown(onKeyDown);
            $(this.element).keyup(onKeyUp);
        }

    };

    $.fn[pluginName] = function ( options ) {
        return this.each(function () {
            if (!$.data(this, 'plugin_' + pluginName)) {
                $.data(this, 'plugin_' + pluginName, new LongPress( this, options ));
            }
        });
    };

}(jQuery, window));