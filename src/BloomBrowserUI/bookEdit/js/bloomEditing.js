// listen for messages sent to this page (from other iframes)
window.addEventListener('message', processExternalMessage, false);
/**
 * Respond to messages from other iframes
 * @param {Event} event
 */
function processExternalMessage(event) {

    var params = event.data.split("\n");

    switch(params[0]) {

        case 'Qtips': // request from accordion to add qtips to marked-up spans
            // q-tips; first 3 are for decodable, last for leveled; could make separate messages.
            var editableElements = $(".bloom-content1");
            editableElements.find('span.' + $.cssSightWord()).each(function() {
                $(this).qtip({ content: 'Sight word' });
            });

            editableElements.find('span.' + $.cssWordNotFound()).each(function() {
                $(this).qtip({ content: 'This word is not decodable in this stage.' });
            });

            // we're considering dropping this entirely
            // We are disabling the "Possible Word" feature at this time.
            //editableElements.find('span.' + $.cssPossibleWord()).each(function() {
            //    $(this).qtip({ content: 'This word is decodable in this stage, but is not part of the collected list of words.' });
            //});

            editableElements.find('span.' + $.cssSentenceTooLong()).each(function() {
                $(this).qtip({ content: 'This sentence is too long for this level.' });
            });
            return;
    }
}

/**
 * Fires an event for C# to handle
 * @param {String} eventName
 * @param {String} eventData
 */
function fireCSharpEditEvent(eventName, eventData) {

    var event = new MessageEvent(eventName, {'view' : window, 'bubbles' : true, 'cancelable' : true, 'data' : eventData});
    document.dispatchEvent(event);
}

$.fn.CenterVerticallyInParent = function() {
    return this.each(function(i) {
        var $this = $(this);
        $this.css('margin-top', 0); // reset before calculating in case of previously messed up page
        var diff = GetDifferenceBetweenHeightAndParentHeight($this);
        if (diff < 0) {
            // we're too big, do nothing to margin-top
            // but the formatButton may need adjusting, in StyleEditor
            return;
        }
        var mh = Math.ceil(diff / 2);
        $(this).css('margin-top', mh);
    });
};

function GetDifferenceBetweenHeightAndParentHeight(jqueryNode) {
    // function also declared and used in StyleEditor
    if (!jqueryNode) {
        return 0;
    }
    return jqueryNode.parent().height() - jqueryNode.height();
}

function isBrOrWhitespace(node) {
    return node && ( (node.nodeType == 1 && node.nodeName.toLowerCase() == "br") ||
           (node.nodeType == 3 && /^\s*$/.test(node.nodeValue) ) );
}

function removeTrailingWhiteSpace(node) {
    if (node && node.nodeType == 3 && node.nodeValue) {
        // Removes one or more (+) whitespace (\s) at the end ($), across multiple lines (m)
        node.nodeValue = node.nodeValue.replace(/\s+$/m, "");
    }
}

function TrimTrailingLineBreaksInDivs(node) {
//    while ( isBrOrWhitespace(node.firstChild) ) {
//        node.removeChild(node.firstChild);
//    }
    while ( isBrOrWhitespace(node.lastChild) ) {
        node.removeChild(node.lastChild);
    }
    // Without this, FF can display a space which isn't a space due to a trailing \r\n
    removeTrailingWhiteSpace(node.lastChild);
}

function CanChangeBookLicense() {

    // First, need to look in .bloomCollection file for <IsSourceCollection> value
    // if 'true', return true.
    var isSource = GetSettings().isSourceCollection;
    if (isSource && isSource.toLowerCase() == 'true') // comes out as capitalized string, if it's there
        return true;

    // meta[@name='lockedDownAsShell' and @content='true'], if exists, return false
    var lockedAsShell = $(document).find('meta[name="lockedDownAsShell"]');
    if (lockedAsShell.length > 0 && lockedAsShell.attr('content').toLowerCase() == 'true')
        return false;
    // meta[@name='canChangeLicense'] and @content='false'], if exists, return false
    var canChange = $(document).find('meta[name="canChangeLicense"]');
    if (canChange.length > 0 && canChange.attr('content').toLowerCase() == 'false')
        return false;

    // Otherwise return true
    return true;
}

//show those bubbles if the item is empty, or if it's not empty, then if it is in focus OR the mouse is over the item
function MakeHelpBubble(targetElement, elementWithBubbleAttributes) {

    var target = $(targetElement);
    var source = $(elementWithBubbleAttributes);

    if (target.css('display') === 'none')
        return; //don't put tips if they can't see it.

    if (target.css('border-bottom-color') === 'transparent')
        return; //don't put tips if they can't edit it. That's just confusing

    var theClasses = 'ui-tooltip-shadow ui-tooltip-plain';

    var pos = {
        at: 'right center',
        my: 'left center',
        viewport: $(window),
        adjust: { method: 'none' }
        };

    // Anybody know why this was here!? BL-1125 complains about this very thing.
    //if (target.hasClass('coverBottomBookTopic'))
    //    pos.adjust = { y: -20 };

    //temporarily disabling this; the problem is that its more natural to put the hint on enclosing 'translationgroup' element, but those elements are *never* empty.
    //maybe we could have this logic, but change this logic so that for all items within a translation group, they get their a hint from a parent, and then use this isempty logic
    //at the moment, the logic is all around whoever has the data-hint
    //var shouldShowAlways = $(this).is(':empty'); //if it was empty when we drew the page, keep the tooltip there
    var shouldShowAlways = true;
    var hideEvents = shouldShowAlways ? false : 'focusout mouseleave';

    // get the default text/stringId
    var whatToSay = target.attr('data-hint');
    if (!whatToSay) whatToSay = source.attr('data-hint');
    if (!whatToSay) whatToSay = source.text();

    // no empty bubbles
    if (!whatToSay) return;

    // determine onFocusOnly
    var onFocusOnly = whatToSay.startsWith('*');
    onFocusOnly = onFocusOnly || source.hasClass('bloom-showOnlyWhenTargetHasFocus') || mightCauseHorizontallyOverlappingBubbles(target);

    // get the localized string
    if (whatToSay.startsWith('*')) whatToSay = whatToSay.substr(1);
    whatToSay = localizationManager.getLocalizedHint(whatToSay, target);

    var functionCall = source.data("functiononhintclick");
    if (functionCall) {
        if (functionCall === 'bookMetadataEditor' && !CanChangeBookLicense())
            return;
        shouldShowAlways = true;

        if (functionCall.indexOf('(') > 0)
            functionCall = 'javascript:' + functionCall + ';';

        whatToSay = "<a href='" + functionCall + "'>" + whatToSay + "</a>";
        hideEvents = false; // Don't specify a hide event...
    }

    if (onFocusOnly) {
        shouldShowAlways = false;
        hideEvents = 'focusout mouseleave';
    }

    target.qtip({
        content: whatToSay,
        position: pos,
        show: {
            event: 'focusin mouseenter',
            ready: shouldShowAlways //would rather have this kind of dynamic thing, but it isn't right: function(){$(this).is(':empty')}//
        }
       , hide: {
           event: hideEvents
       },
        style: {
            classes: theClasses
        }
    });
}

function Cleanup() {

    // for stuff bloom introduces, just use this "bloom-ui" class to have it removed
    $(".bloom-ui").each(function() {
        $(this).remove();
    });

    // remove the div's which qtip makes for the tips themselves
    $("div.qtip").each(function() {
        $(this).remove();
    });

    // remove the attributes qtips adds to the things being annotated
    $("*[aria-describedby]").each(function() {
        $(this).removeAttr("aria-describedby");
    });
    $("*[ariasecondary-describedby]").each(function() {
        $(this).removeAttr("ariasecondary-describedby");
    });
    $("*.editTimeOnly").remove();
    $("*.dragHandle").remove();
    $("*").removeAttr("data-easytabs");

    $("div.ui-resizable-handle").remove();
    $('div, figure').each(function() {
        $(this).removeClass('ui-draggable');
        $(this).removeClass('ui-resizable');
        $(this).removeClass('hoverUp');
    });

    $('button').each(function () {
            $(this).remove();
    });


  $('div.bloom-editable').each( function() {
    TrimTrailingLineBreaksInDivs(this);
    });


    cleanupImages();
    cleanupOrigami();
}



function AddExperimentalNotice(element) {
    $(element).qtipSecondary({
        content: "<div id='experimentNotice'><img src='/bloom/images/experiment.png'/>This page is an experimental prototype which may have many problems, for which we apologize.<div/>"
                         , show: { ready: true }
                         , hide: false
                         , position: { at: 'right top',
                             my: 'left top'
                         },
        style: { classes: 'ui-tooltip-red',
            tip: { corner: false }
        }
    });
}

function GetStyleClassFromElement(element) {
    var c = $(element).attr("class");
    if (!c)
        c = "";
    var classes = c.split(' ');

    for (var i = 0; i < classes.length; i++) {
        if (classes[i].indexOf('-style') > 0) {
            return classes[i];
        }
    }
    return null;
}

//:empty is not quite enough... we don't want to show bubbles if all there is is an empty paragraph
jQuery.expr[':'].hasNoText = function (obj) {
    return jQuery.trim(jQuery(obj).text()).length == 0;
};

 //Sets up the (currently yellow) qtip bubbles that give you the contents of the box in the source languages
function MakeSourceTextDivForGroup(group) {

    var divForBubble = $(group).clone();
    $(divForBubble).removeAttr('style');

    //make the source texts in the bubble read-only and remove any user font size adjustments
    $(divForBubble).find("textarea, div").each(function() {
        $(this).attr("readonly", "readonly");
        $(this).removeClass('bloom-editable');
        $(this).removeClass('overflow'); // don't want red in source text bubbles
        $(this).attr("contenteditable", "false");
        var styleClass = GetStyleClassFromElement(this);
        if (styleClass)
            $(this).removeClass(styleClass);
        $(this).addClass("source-text");
    });

    var vernacularLang = localizationManager.getVernacularLang();

    $(divForBubble).removeClass(); //remove them all
    $(divForBubble).addClass("ui-sourceTextsForBubble");
    //don't want empty items in the bubble
    //N.B. :hasNoText is defined above
    $(divForBubble).find("textarea:empty, div:hasNoText").each(function() {
        $(this).remove();
    });

    //don't want the vernacular or languages in use for bilingual/trilingual boxes to be shown in the bubble
    $(divForBubble).find("*.bloom-content1, *.bloom-content2, *.bloom-content3").each(function () {
        $(this).remove();
    });

    //in case some formatting didn't get cleaned up
    StyleEditor.CleanupElement(divForBubble);

    //if there are no languages to show in the bubble, bail out now
    if ($(divForBubble).find("textarea, div").length == 0)
        return;

/* removed june 12 2013 was dying with new jquery as this was Window and that had no OwnerDocument    $(this).after(divForBubble);*/

    var selectorOfDefaultTab="li:first-child";

    //make the li's for the source text elements in this new div, which will later move to a tabbed bubble
    $(divForBubble).each(function () {
        $(this).prepend('<ul class="editTimeOnly bloom-ui"></ul>');
        var list = $(this).find('ul');
        //nb: Jan 2012: we modified "jquery.easytabs.js" to target @lang attributes, rather than ids.  If that change gets lost,
        //it's just a one-line change.
        var items = $(this).find("textarea, div");
        items.sort(function(a, b) {
            var keyA = $(a).attr('lang');
            var keyB = $(b).attr('lang');
            if (keyA === vernacularLang)
                return -1;
            if (keyB === vernacularLang)
                return 1;
            if (keyA < keyB)
                return -1;
            if (keyA > keyB)
                return 1;
            return 0;
        });
        var shellEditingMode = false;
        items.each(function() {
            var iso = $(this).attr('lang');
            if (iso) {
                var languageName = localizationManager.getLanguageName(iso);
                if (!languageName)
                    languageName = iso;
                var shouldShowOnPage = (iso === vernacularLang) /* could change that to 'bloom-content1' */ || $(this).hasClass('bloom-contentNational1') || $(this).hasClass('bloom-contentNational2') || $(this).hasClass('bloom-content2') || $(this).hasClass('bloom-content3');

                // in translation mode, don't include the vernacular in the tabs, because the tabs are being moved to the bubble
                if (iso !== "z" && (shellEditingMode || !shouldShowOnPage)) {

                    $(list).append('<li id="' + iso + '"><a class="sourceTextTab" href="#' + iso + '">' + languageName + '</a></li>');
                    if (iso === GetSettings().defaultSourceLanguage) {
                        selectorOfDefaultTab = "li#" + iso; //selectorOfDefaultTab="li:#"+iso; this worked in jquery 1.4
                    }
                }
            }
        });
    });

    //now turn that new div into a set of tabs
    // Review: as of 9 May 2014 the tab links have turned into bulleted links
    if ($(divForBubble).find("li").length > 0) {
        $(divForBubble).easytabs({
            animate: false,
            defaultTab: selectorOfDefaultTab
        });
//        $(divForBubble).bind('easytabs:after', function(event, tab, panel, settings){
//            alert(panel.selector)
//        });

  }
  else {
    $(divForBubble).remove();//no tabs, so hide the bubble
    return;
    }

    var showEvents = false;
    var hideEvents = false;
    var shouldShowAlways = true;


    if(mightCauseHorizontallyOverlappingBubbles(group)) {
        showEvents = 'focusin';
        hideEvents = 'focusout';
        shouldShowAlways = false;
    }

    // turn that tab thing into a bubble, and attach it to the original div ("group")
    $(group).each(function () {
      // var targetHeight = Math.max(55, $(this).height()); // This ensures we get at least one line of the source text!

      $(this).qtip({
          position: {
                my: 'left top',
                at: 'right top',
              adjust: {
                  x: 10,
                  y: 0
              }
          },
          content: $(divForBubble),

          show: {
              event: showEvents,
              ready: shouldShowAlways
          },
          style: {
                tip: {
                    corner: true,
                    width: 10,
                    height: 10
                },
              classes: 'ui-tooltip-green ui-tooltip-rounded uibloomSourceTextsBubble'
          },
          hide: hideEvents
      });
  });
}

function mightCauseHorizontallyOverlappingBubbles(element) {
    //we can't actually know for sure if overlapping would happen, but
    //we can be very conservative and say that if the text
    //box isn't taking up the whole width, it *might* cause
    //an overlap
    if($(element).hasClass('bloom-alwaysShowBubble')) {
        return false;
    }
    var availableWidth = $(element).closest(".marginBox").width();
    var kTolerancePixels = 10; //if the box is just a tiny bit smaller, there's not going to be anything to overlap
    return $(element).width() < (availableWidth - kTolerancePixels);
}

//add a delete button which shows up when you hover
function SetupDeletable(containerDiv) {
    $(containerDiv).mouseenter(
        function () {
            var button = $("<button class='deleteButton smallImageButton' title='Delete'></button>");
            $(button).click(function(){
                $(containerDiv).remove()});
            $(this).prepend(button);
        })
        .mouseleave(function () {
            $(this).find(".deleteButton").each(function () {
                $(this).remove()
            });
        });

    return $(containerDiv);
}

// Add various editing key handlers
function AddEditKeyHandlers(container) {
    //Make F6 apply a superscript style (later we'll change to ctrl+shift+plus, as word does. But capturing those in js by hand is a pain.
    //nb: we're avoiding ctrl+plus and ctrl+shift+plus (as used by MS Word), because they means zoom in browser. also three keys is too much
    $(container).find("div.bloom-editable").on('keydown', null, 'F6', function (e) {
        var selection = document.getSelection();
        if (selection) {
            //NB: by using exeCommand, we get undo-ability
            document.execCommand("insertHTML", false, "<span class='superscript'>" + document.getSelection() + "</span>");
        }
    });

    $(container).find("div.bloom-editable").on('keydown', null, 'ALT+CTRL+0', function (e) {//ctrl alt 0 is from google drive for "normal text"
        e.preventDefault();
        document.execCommand("formatBlock", false, "P");
    });

    // Make F7 apply top-level header style (H1)
    $(container).find("div.bloom-editable").on('keydown', null, 'F7', function (e) {
        e.preventDefault();
        document.execCommand("formatBlock", false, "H1");
    });
    $(container).find("div.bloom-editable").on('keydown', null, 'ALT+CTRL+1', function (e) {//ctrl alt 1 is from google drive
        e.preventDefault();
        document.execCommand("formatBlock", false, "H1");
    });

    // Make F8 apply header style (H2)
    $(container).find("div.bloom-editable").on('keydown', null, 'F8', function (e) {
        e.preventDefault();
        document.execCommand("formatBlock", false, "H2");
    });
    $(container).find("div.bloom-editable").on('keydown', null, 'ALT+CTRL+2', function (e) { //ctrl alt 2 is from google drive
        e.preventDefault();
        document.execCommand("formatBlock", false, "H2");
    });

    $(document).bind('keydown', 'ctrl+space', function (e) {
      e.preventDefault();
      document.execCommand("removeFormat", false, false);//will remove bold, italics, etc. but not things that use elements, like h1
    });

    $(document).bind('keydown', 'ctrl+u', function (e) {
      e.preventDefault();
      document.execCommand("underline", null, null);
    });
    $(document).bind('keydown', 'ctrl+b', function (e) {
      e.preventDefault();
      document.execCommand("bold", null, null);
    });
    $(document).bind('keydown', 'ctrl+i', function (e) {
      e.preventDefault();
      document.execCommand("italic", null, null);
    });
    //note: these have the effect of introducing a <div> inside of the div.bloom-editable we're in.
    $(document).bind('keydown', 'ctrl+r', function (e) {
        e.preventDefault();
        document.execCommand("justifyright", false, null);
    });
    $(document).bind('keydown', 'ctrl+l', function (e) {
        e.preventDefault();
        document.execCommand("justifyleft", false, null);
    });
    $(document).bind('keydown', 'ctrl+shift+e', function (e) { //ctrl+shiift+e is what google drive uses
        e.preventDefault();
        document.execCommand("justifycenter", false, null);
    });
}

// Add little language tags
function AddLanguageTags(container) {
    $(container).find(".bloom-editable:visible[contentEditable=true]").each(function () {
        var $this = $(this);

        // If this DIV already had a language tag, remove the content in case we decide the situation has changed.
        if ($this.hasAttr('data-languageTipContent')) {
            $this.removeAttr('data-languageTipContent');
        }

        // With a really small box that also had a hint qtip, there wasn't enough room and the two fought
        // with each other, leading to flashing back and forth
        // Of course that was from when Language Tags were qtips too, but I think I'll leave the restriction for now.
        if ($this.width() < 100) {
            return;
        }

        // Make sure language tags appear or disappear depending on what edit mode we are in
        var isTranslationMode = IsInTranslationMode();
        if (isTranslationMode && $this.hasClass('bloom-readOnlyInTranslationMode')) {
            return;
        }
        if (!isTranslationMode && $this.hasClass('bloom-readOnlyInEditMode')) {
            return;
        }

        var key = $this.attr("lang");
        if (key == "*" || key.length < 1)
            return; //seeing a "*" was confusing even to me

        // if this or any parent element has the class bloom-hideLanguageNameDisplay, we don't want to show any of these tags
        // first usage (for instance) was turning off language tags for a whole page
        if ($this.hasClass('bloom-hideLanguageNameDisplay') || $this.parents('.bloom-hideLanguageNameDisplay').length != 0) {
            return;
        }

        var whatToSay = localizationManager.getText(key);
        if (!whatToSay)
            whatToSay = key; //just show the code

        // Put whatToSay into data attribute for pickup by the css
        $this.attr('data-languageTipContent', whatToSay);
    });
}

// Add (yellow) hint bubbles from (usually) label.bubble elements
function AddHintBubbles(container) {
    //Handle <label>-defined hint bubbles on mono fields, that is divs that aren't in the context of a
    //bloom-translationGroup (those should have a single <label> for the whole group).
    //Notice that the <label> inside an editable div is in a precarious position, it could get
    //edited away by the user. So we are moving the contents into a data-hint attribute on the field.
    //Yes, it could have been placed there in the 1st place, but the <label> approach is highly readable,
    //so it is preferred when making new templates by hand.
    $(container).find(".bloom-editable:visible label.bubble").each(function () {
        var labelElement = $(this);
        var whatToSay = labelElement.text();
        if (!whatToSay)
            return;
        var onFocusOnly = labelElement.hasClass('bloom-showOnlyWhenTargetHasFocus');

        var enclosingEditableDiv = labelElement.parent();
        enclosingEditableDiv.attr('data-hint', labelElement.text());
        labelElement.remove();

        //attach the bubble, this editable only, then remove it
        MakeHelpBubble($(enclosingEditableDiv), labelElement, whatToSay, onFocusOnly);
    });

    // Having a <label class='bubble'> inside a div.bloom-translationGroup gives a hint bubble outside each of
    // the fields, with some template-filling and localization for each.
    // Note that in Version 1.0, we didn't have this <label> ability but we had @data-hint.
    // Using <label> instead of the attribute makes the html much easier to read, write, and add additional
    // behaviors through classes
    $(container).find(".bloom-translationGroup > label.bubble").each(function () {
        var labelElement = $(this);
        var whatToSay = labelElement.text();
        if (!whatToSay)
            return;
        var onFocusOnly = labelElement.hasClass('bloom-showOnlyWhenTargetHasFocus');

        //attach the bubble, separately, to every visible field inside the group
        labelElement.parent().find("div.bloom-editable:visible").each(function () {
            MakeHelpBubble($(this), labelElement, whatToSay, onFocusOnly);
        });
    });

    $(container).find("*.bloom-imageContainer > label.bubble").each(function () {
        var labelElement = $(this);
        var imageContainer = $(this).parent();
        var whatToSay = labelElement.text();
        if (!whatToSay)
            return;
        var onFocusOnly = labelElement.hasClass('bloom-showOnlyWhenTargetHasFocus');
        MakeHelpBubble(imageContainer, labelElement, whatToSay, onFocusOnly);
    });

    //This is the "low-level" way to get a hint bubble, cramming it all into a data-hint attribute.
    //It is used by the "high-level" way in the monolingual case where we don't have a bloom-translationGroup,
    //and need a place to preserve the contents of the <label>, which is in danger of being edited away.
    $(container).find("*[data-hint]").each(function () {
        var whatToSay = $(this).attr("data-hint");//don't use .data(), as that will trip over any } in the hint and try to interpret it as json
        if (!whatToSay)
            return;

        //make hints that start with a * only show when the field has focus
        var showOnFocusOnly = whatToSay.startsWith("*");

        if (whatToSay.startsWith("*")) {
            whatToSay = whatToSay.substring(1, 1000);
        }

        if (whatToSay.length == 0 || $(this).css('display') == 'none')
            return;

        MakeHelpBubble($(this), $(this), whatToSay, showOnFocusOnly);
    });
}

// This function is called directly from EditingView.OnShowBookMetadataEditor()
function SetCopyrightAndLicense(data) {
    //nb: for textarea, we need val(). But for div, it would be text()
    $("DIV[data-book='copyright']").text(DecodeHtml(data.copyright));
    $("DIV[data-book='licenseUrl']").text(data.licenseUrl);
    $("DIV[data-book='licenseDescription']").text(data.licenseDescription);
    $("DIV[data-book='licenseNotes']").text(DecodeHtml(data.licenseNotes));
    var licenseImageValue = data.licenseImage + "?" + new Date().getTime(); //the time thing makes the browser reload it even if it's the same name
    if (data.licenseImage.length == 0) {
        licenseImageValue = ""; //don't wan the date on there
        $("IMG[data-book='licenseImage']").attr('alt', '');
    }

    $("IMG[data-book='licenseImage']").attr("src", licenseImageValue);
    SetBookCopyrightAndLicenseButtonVisibility($('body'));
}

function SetBookCopyrightAndLicenseButtonVisibility(container) {
    var shouldShowButton = !($(container).find("DIV.copyright").text());
    $(container).find("button#editCopyrightAndLicense").css("display", shouldShowButton ? "inline" : "none");
}

function DecodeHtml(encodedString) {
    return encodedString.replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&#39;/g, "'").replace(/&#169;/g, "©");
}

function GetEditor() {
    return new StyleEditor("/bloom/bookEdit");
}

function GetOverflowChecker() {
    return new OverflowChecker();
}

function IsInTranslationMode() {
    var body = $("body");
    if (!body.hasAttr('editMode'))
        return false;
    else {
        return body.attr('editMode') == "translation";
    }
}

$.fn.hasAttr = function (name) {
    var attr = $(this).attr(name);

    // For some browsers, `attr` is undefined; for others,
    // `attr` is false.  Check for both.
    return (typeof attr !== 'undefined' && attr !== false);
};



// Originally, all this code was in document.load and the selectors were acting
// on all elements (not bound by the container).  I added the container bound so we
// can add new elements (such as during layout mode) and call this on only newly added elements.
// Now document.load calls this with $('body') as the container.
// REVIEW: Some of these would be better off in OneTimeSetup, but too much risk to try to decide right now.
function SetupElements(container) {

    SetupImagesInContainer(container);

    //add a marginBox if it's missing. We introduced it early in the first beta
    $(container).find(".bloom-page").each(function () {
        if ($(this).find(".marginBox").length == 0) {
            $(this).wrapInner("<div class='marginBox'></div>");
        }
    });
    $(container).find(".bloom-editable").each(function () { BloomField.ManageField(this); });

    //make textarea edits go back into the dom (they were designed to be POST'ed via forms)
    $(container).find("textarea").blur(function () {
        this.innerHTML = this.value;
    });

    var accordion = parent.window.document.getElementById("accordion");
    var model;

    // accordion will be undefined during unit testing
    if (accordion) model = accordion.contentWindow['model'];

    // model will be undefined if the reader tools are not loaded
    if (model) {

        // invoke function when a bloom-editable element loses focus.
        $(container).find('.bloom-editable').focusout(function () {
            model.doMarkup();
        });

        $(container).find('.bloom-editable').focusin(function () {
            model.noteFocus(this); // 'This' is the element that just got focus.
        });

        // and a slightly different one for keypresses
        $(container).find('.bloom-editable').keypress(function () {
            model.doKeypressMarkup();
        });

        $(container).find('.bloom-editable').keydown(function (e) {
            if ((e.keyCode == 90 || e.keyCode == 89) && e.ctrlKey) { // ctrl-z or ctrl-Y
                if (model.currentMarkupType !== MarkupType.None) {
                    e.preventDefault();
                    if (e.shiftKey || e.keyCode == 89) { // ctrl-shift-z or ctrl-y
                        model.redo();
                    }
                    else {
                        model.undo();
                    }
                    return false;
                }
            }
        });
    }

    SetBookCopyrightAndLicenseButtonVisibility(container);

    //CSS normally can't get at the text in order to, for example, show something different if it is empty.
    //This allows you to add .bloom-needs-data-text to a bloom-translationGroup in order to get
    //its child bloom-editable's to have data-texts's on them
    $(container).find(".bloom-translationGroup.bloom-text-for-css .bloom-editable").each(function () {
        // initially fill it
        $(this).attr('data-text', this.textContent);
        // keep it up to date
        $(this).on('blur paste input', function () {
            $(this).attr('data-text', this.textContent);
        });
    });

    //in bilingual/trilingual situation, re-order the boxes to match the content languages, so that stylesheets don't have to
    $(container).find(".bloom-translationGroup").each(function () {
        var contentElements = $(this).find("textarea, div.bloom-editable");
        contentElements.sort(function (a, b) {
            //using negatives so that something with none of these labels ends up with a > score and at the end
            var scoreA = $(a).hasClass('bloom-content1') * -3 + ($(a).hasClass('bloom-content2') * -2) + ($(a).hasClass('bloom-content3') * -1);
            var scoreB = $(b).hasClass('bloom-content1') * -3 + ($(b).hasClass('bloom-content2') * -2) + ($(b).hasClass('bloom-content3') * -1);
            if (scoreA < scoreB)
                return -1;
            if (scoreA > scoreB)
                return 1;
            return 0;
        });
        //do the actual rearrangement
        $(this).append(contentElements);
    });

    //Convert Standard Format Markers in the pasted text to html spans
    $(container).find("div.bloom-editable").on("paste", function (e) {
        if (!e.originalEvent.clipboardData)
            return;

        var s = e.originalEvent.clipboardData.getData('text/plain');
        if (s==null || s =='')
            return;

        var re = new RegExp('\\\\v\\s(\\d+)', 'g');
        var matches = re.exec(s);
        if (matches == null) {
            //just let it paste
        }
        else {
            e.preventDefault();
            var x =s.replace(re, "<span class='superscript'>$1</span>");
            document.execCommand("insertHtml", false, x);
            //NB: this would undo, but it doesn't work document.execCommand("paste", false, x);
        }
    });

    AddEditKeyHandlers(container);

    //--------------------------------
    //keep divs vertically centered (yes, I first tried *all* the css approaches, they don't work for our situation)

    //do it initially
    $(container).find(".bloom-centerVertically").CenterVerticallyInParent();
    //reposition as needed
    $(container).find(".bloom-centerVertically").resize(function () { //nb: this uses a 3rd party resize extension from Ben Alman; the built in jquery resize only fires on the window
        $(this).CenterVerticallyInParent();
    });

    AddHintBubbles(container);

    //html5 provides for a placeholder attribute, but not for contenteditable divs like we use.
    //So one of our foundational stylesheets looks for @data-placeholder and simulates the
    //@placeholder behavior.
    //Now, what's going on here is that we also support
    //<label class='placeholder'> inside a div.bloom-translationGroup to get this placeholder
    //behavior on each of the fields inside the group .
    //Using <label> instead of the attribute makes the html much easier to read, write, and add additional
    //behaviors through classes.
    //So the job of this bit here is to take the label.placeholder and create the data-placeholders.
    $(container).find("*.bloom-translationGroup > label.placeholder").each(function () {

        var labelText = $(this).text();

        //put the attributes on the individual child divs
        $(this).parent().find('.bloom-editable').each(function () {

            //enhance: it would make sense to allow each of these to be customized for their div
            //so that you could have a placeholder that said "Name in {lang}", for example.
            $(this).attr('data-placeholder', labelText);
            //next, it's up to CSS to draw the placeholder when the field is empty.
        });
    });

  

    $(container).find('div.bloom-editable').each(function () {
        $(this).attr('contentEditable', 'true');
    });

    // Bloom needs to make some fields readonly. E.g., the original license when the user is translating a shellbook
    // Normally, we'd control this is a style in editTranslationMode.css/editOriginalMode.css. However, "readonly" isn't a style, just
    // an attribute, so it can't be included in css.
    // The solution here is to add the readonly attribute when we detect that the css has set the cursor to "not-allowed".
    $(container).find('textarea, div').focus(function () {
        //        if ($(this).css('border-bottom-color') == 'transparent') {
        if ($(this).css('cursor') == 'not-allowed') {
            $(this).attr("readonly", "true");
            $(this).removeAttr("contentEditable");
        }
        else {
            $(this).removeAttr("readonly");
            //review: do we need to add contentEditable... that could lead to making things editable that shouldn't be
        }
    });

    AddLanguageTags(container);

    // If the user moves over something they can't edit, show a tooltip explaining why not
    $(container).find('*[data-hint]').each(function () {

        if ($(this).css('cursor') == 'not-allowed') {
            var whyDisabled = "You cannot change these because this is not the original copy.";
            if ($(this).hasClass('bloom-readOnlyInEditMode')) {
                whyDisabled = "You cannot put anything in there while making an original book.";
            }

            var whatToSay = $(this).attr("data-hint");//don't use .data(), as that will trip over any } in the hint and try to interpret it as json

            whatToSay = localizationManager.getLocalizedHint(whatToSay, $(this)) + " <br/>" + whyDisabled;
            var theClasses = 'ui-tooltip-shadow ui-tooltip-red';
            var pos = { at: 'right center',
                my: 'left center'
            };
            $(this).qtip({
                content: whatToSay,
                position: pos,
                show: {
                    event: 'focusin mouseenter'
                },
                style: {
                    classes: theClasses
                }
            });
        }
    });

    //Same thing for divs which are potentially editable, but via the contentEditable attribute instead of TextArea's ReadOnly attribute
    // editTranslationMode.css/editOriginalMode.css can't get at the contentEditable (css can't do that), so
    // so they set the cursor to "not-allowed", and we detect that and set the contentEditable appropriately
    $(container).find('div.bloom-readOnlyInTranslationMode').focus(function () {
        if ($(this).css('cursor') == 'not-allowed') {
            $(this).removeAttr("contentEditable");
        }
        else {
            $(this).attr("contentEditable", "true");
        }
    });

    //first used in the Uganda SHRP Primer 1 template, on the image on day 1
    $(container).find(".bloom-draggableLabel").each(function () {
        // previous to June 2014, containment was not working, so some items may be
        // out of bounds. Or the stylesheet could change the size of things. This gets any such back in bounds.
        if ($(this).position().left < 0) {
            $(this).css('left', 0);
        }
        if ($(this).position().top < 0) {
            $(this).css('top', 0);
        }
        if ($(this).position().left + $(this).width() > $(this).parent().width()) {
            $(this).css('left', $(this).parent().width() - $(this).width());
        }
        if ($(this).position().top > $(this).parent().height()) {
            $(this).css('top', $(this).parent().height() - $(this).height());
        }

        $(this).draggable(
        {
            containment: "parent", //NB: this containment is of the translation group, not the editable inside it. So avoid margins on the translation group.
            handle: '.dragHandle'
        });
    });


    $(container).find(".bloom-draggableLabel")
       .mouseenter(function () {
        $(this).prepend(" <div class='dragHandle'></div>");
    });

    $(container).find(".bloom-draggableLabel").mouseleave(function () {
        $(this).find(".dragHandle").each(function() {
            $(this).remove()
        });
    });

    // add drag and resize ability where elements call for it
    //   $(".bloom-draggable").draggable({containment: "parent"});
    $(container).find(".bloom-draggable").draggable({ containment: "parent",
        handle: '.bloom-imageContainer',
        stop: function (event, ui) {
            $(this).find('.wordsDiv').find('div').each(function () {
                $(this).qtip('reposition');
            })
        } //yes, this repositions *all* qtips on the page. Yuck.
    }); //without this "handle" restriction, clicks on the text boxes don't work. NB: ".moveButton" is really what we wanted, but didn't work, probably because the button is only created on the mouseEnter event, and maybe that's too late.
    //later note: using a real button just absorbs the click event. Other things work better
    //http://stackoverflow.com/questions/10317128/how-to-make-a-div-contenteditable-and-draggable

    /* Support in page combo boxes that set a class on the parent, thus making some change in the layout of the pge.
    Example:
         <select name="Story Style" class="bloom-classSwitchingCombobox">
             <option value="Fictional">Fiction</option>
             <option value="Informative">Informative</option>
     </select>
     */
    //First we select the initial value based on what class is currently set, or leave to the default if none of them
    $(container).find(".bloom-classSwitchingCombobox").each(function(){
        //look through the classes of the parent for any that match one of our combobox values
        var i;
        for(i=0; i< this.options.length;i++) {
            var c = this.options[i].value;
            if($(this).parent().hasClass(c)){
                $(this).val(c);
                break;
            }
        }
    });
    //And now we react to the user choosing a different value
    $(container).find(".bloom-classSwitchingCombobox").change(function(){
        //remove any of the values that might already be set
        var i;
        for(i=0; i< this.options.length;i++) {
            var c = this.options[i].value;
            $(this).parent().removeClass(c);
        }
        //add back in the one they just chose
        $(this).parent().addClass(this.value);
    });

    //only make things deletable if they have the deletable class *and* page customization is enabled
    $(container).find("DIV.bloom-page.bloom-enablePageCustomization DIV.bloom-deletable").each(function () {
        SetupDeletable(this);
    });

    $(container).find(".pictureDictionaryPage").each(function () {
        AddExperimentalNotice(this);
    });

    $(container).find(".bloom-resizable").each(function () {
        SetupResizableElement(this);
    });

    SetOverlayForImagesWithoutMetadata(container);

    //note, the normal way is for the user to click the link on the qtip.
    //But clicking on the existing topic may be natural too, and this prevents
    //them from editing it by hand.
    $(container).find("div[data-book='topic']").click(function () {
        if ($(this).css('cursor') == 'not-allowed')
            return;
        TopicChooser.showTopicChooser();
    });

    // Copy source texts out to their own div, where we can make a bubble with tabs out of them
    // We do this because if we made a bubble out of the div, that would suck up the vernacular editable area, too,
    if ($(container).find(".bloom-preventSourceBubbles").length == 0) {
        $(container).find("*.bloom-translationGroup").not(".bloom-readOnlyInTranslationMode").each(function() {
            if ($(this).find("textarea, div").length > 1) {
                MakeSourceTextDivForGroup(this);
            }
        });
    }

    // Add overflow event handlers so that when a div is overfull,
    // we add the overflow class and it gets a red background or something
    // Moved AddOverflowHandlers() after SetupImage() because some pages with lots of placeholders
    // were prematurely overflowing before the images were set to the right size.
    GetOverflowChecker().AddOverflowHandlers(container);

    var editor = GetEditor();

    $(container).find("div.bloom-editable:visible").each(function () {
        // If the .bloom-editable or any of its ancestors (including <body>) has the class "bloom-userCannotModifyStyles",
        // then the controls that allow the user to adjust the styles will not be shown.This does not prevent the user
        // from doing character styling, e.g. CTRL+b for bold.
        if ($(this).closest('.bloom-userCannotModifyStyles').length == 0) {
            $(this).focus(function() {
                editor.AttachToBox(this);
            });
        }
    });

    getIframeChannel().simpleAjaxGet('/bloom/windows/useLongpress', function(response) {
        if (response === 'Yes')
            $(container).find('.bloom-editable').longPress();
    });

    //When we do a CTRL+A DEL, FF leaves us with a <br></br> at the start. When the first key is then pressed,
    //a blank line is shown and the letter pressed shows up after that.
    //This detects that situation when we type the first key after the deletion, and first deletes the <br></br>.
    $(container).find('.bloom-editable').keypress(function (event) {
        //this is causing a worse problem, (preventing us from typing empty lines to move the start of the text down), so we're going to live with the empty space for now.
        // TODO: perhaps we can act when the DEL or Backspace occurs and then detect this situation and clean it up.
//         if ($(event.target).text() == "") { //NB: the browser inspector shows <br></br>, but innerHTML just says "<br>"
//            event.target.innerHTML = "";
//        }
    });
    //This detects that situation when we do CTRL+A and then type a letter, instead of DEL
    $(container).find('.bloom-editable').keyup(function (event) {
        //console.log(event.target.innerHTML);
        // If they pressed a letter instead of DEL, we get this case:
        if ($(event.target).find("#formatButton").length == 0) { //NB: the browser inspector shows <br></br>, but innerHTML just says "<br>"
            //they have also deleted the formatButton, so put it back in
            // console.log('attaching'); REVIEW: this shows that we're doing the attaching on the first character entered, even though it appears the editor was already attached.
            //so we actually attach twice. That's ok, the editor handles that, but I don't know why we're passing the if, and it could be improved.
            if ($(this).closest('.bloom-userCannotModifyStyles').length == 0)
                editor.AttachToBox(this);
        }
        else {
            // already have a format cog, better make sure it's in the right place
            editor.AdjustFormatButton($(this));
        }
    });

    // focus on the first editable field
    // HACK for BL-1139: except for some reason when the Reader tools are active this causes
    // quick typing on a newly loaded page to get the cursor messed up. So for the Reader tools, the
    // user will need to actually click in the div to start typing.
    if (!model)
        $(container).find("textarea, div.bloom-editable").first().focus(); //review: this might choose a textarea which appears after the div. Could we sort on the tab order?
}

// Only put setup code here which is guaranteed to only be run once per page load.
// e.g. Don't put setup for elements such as image containers or editable boxes which may get added after page load.
function OneTimeSetup() {
    setupOrigami();
}


// ---------------------------------------------------------------------------------
// document ready function
// ---------------------------------------------------------------------------------
$(document).ready(function() {
    if($.fn.qtip)
        $.fn.qtip.zindex = 15000;
    //gives an error $.fn.qtip.plugins.modal.zindex = 1000000 - 20;

    $.fn.reverse = function () {
        return this.pushStack(this.get().reverse(), arguments);
    };

    //if this browser doesn't have endsWith built in, add it
    if (typeof String.prototype.endsWith !== 'function') {
        String.prototype.endsWith = function (suffix) {
            return this.indexOf(suffix, this.length - suffix.length) !== -1;
        };
    }

    /* Defines a starts-with function*/
    if (typeof String.prototype.startsWith != 'function') {
        String.prototype.startsWith = function (str) {
            return this.indexOf(str) == 0;
        };
    }

    //eventually we want to run this *after* we've used the page, but for now, it is useful to clean up stuff from last time
    Cleanup();

   SetupElements($('body'));
   OneTimeSetup();

    //this is some sample code for working on CommandAvailabilityPublisher websocket messages
//   var client = new WebSocket("ws://127.0.0.1:8189");
//   client.onmessage = function(event) {
//        var commandStatus = JSON.parse(event.data);
//        alert("DeleteCurrentPage Command "+ (commandStatus.deleteCurrentPage.enabled == true ? "Enabled" : "Disabled")) ;
//    }
}); // end document ready function
