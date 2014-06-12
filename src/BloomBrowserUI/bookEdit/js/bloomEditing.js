$.fn.CenterVerticallyInParent = function() {
    return this.each(function(i) {
        var ah = $(this).height();
        var ph = $(this).parent().height();
        var mh = Math.ceil((ph - ah) / 2);
        $(this).css('margin-top', mh);

        ///There is a bug in wkhtmltopdf where it determines the height of these incorrectly, causing, in a multlingual situation, the 1st text box to hog up all the room and
        //push the other guys off the page. So the hack solution of the moment is to remember the correct height here, in gecko-land, and use it over there to set the max-height.
        //See bloomPreview.SetMaxHeightForHtmlToPDFBug()
        $(this).children().each(function(){
            var h= $(this).height();
            $(this).attr('data-firefoxHeight', h);
        });
    });
};


function isBrOrWhitespace(node) {
    return node && ( (node.nodeType == 1 && node.nodeName.toLowerCase() == "br") ||
           (node.nodeType == 3 && /^\s*$/.test(node.nodeValue) ) );
}

function TrimTrailingLineBreaksInDivs(node) {
    while ( isBrOrWhitespace(node.firstChild) ) {
        node.removeChild(node.firstChild);
    }
    while ( isBrOrWhitespace(node.lastChild) ) {
        node.removeChild(node.lastChild);
    }
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
function MakeHelpBubble(targetElement, elementWithBubbleAttributes, whatToSay, onFocusOnly) {

    if ($(targetElement).css('display') == 'none') {
        return;
    }

    if ($(targetElement).css('border-bottom-color') == 'transparent') {
        return; //don't put tips if they can't edit it. That's just confusing
    }
    if ($(targetElement).css('display') == 'none') {
        return; //don't put tips if they can't see it.
    }

    theClasses = 'ui-tooltip-shadow ui-tooltip-plain';
    if ($(targetElement).height() < 100) {
        pos = {
            at: 'right center', //I like this, but it doesn't reposition well -->'right center',
            my: 'left center' //I like this, but it doesn't reposition well-->  'left center',
           , viewport: $(window)
            // , adjust: { y: -20 }
        };
    }
    else { // with the big back covers, the adjustment just makes things worse.
        pos = {
            at: 'right center',
            my: 'left center'
        };
    }

    //temporarily disabling this; the problem is that its more natural to put the hint on enclosing 'translationgroup' element, but those elements are *never* empty.
    //maybe we could have this logic, but change this logic so that for all items within a translation group, they get their a hint from a parent, and then use this isempty logic
    //at the moment, the logic is all around whoever has the data-hint
    //var shouldShowAlways = $(this).is(':empty'); //if it was empty when we drew the page, keep the tooltip there
    var shouldShowAlways = true;
    var hideEvents = shouldShowAlways ? null : 'focusout mouseleave';

    var functionCall = $(elementWithBubbleAttributes).data("functiononhintclick");
    if (functionCall) {
        if (functionCall == 'bookMetadataEditor' && !CanChangeBookLicense())
            return;
        shouldShowAlways = true;
        whatToSay = "<a href='" + functionCall + "'>" + whatToSay + "</a>";
        hideEvents = false; // Don't specify a hide event...
    }

    if (onFocusOnly) {
        shouldShowAlways = false;
        hideEvents = 'focusout mouseleave';
    }

    whatToSay = GetLocalizedHint(whatToSay, $(targetElement));

    $(targetElement).qtip({
        content: whatToSay,
        position: pos,
        show: {
            event: 'focusin mouseenter',
            ready: shouldShowAlways //would rather have this kind of dynamic thing, but it isn't right: function(){$(this).is(':empty')}//
        }
       , tip: { corner: 'left center' }
       , hide: {
           event: hideEvents
       },
        adjust: { method: 'flip none' },
        style: {
            classes: theClasses
        }
    });
}

function Cleanup() {

        //for stuff bloom introduces, just use this "bloom-ui" class to have it removed
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

  $('.bloom-imageContainer').css('opacity', '');//comes in on img containers from an old version of myimgscale, and is a major problem if the image is missing
    $('.bloom-imageContainer').css('overflow', '');//review: also comes form myimgscale; is it a problem?
}

 //Make a toolbox off to the side (implemented using qtip), with elements that can be dragged
 //onto the page
function AddToolbox(){
    $('div.bloom-page.bloom-enablePageCustomization').each(function () {
        $(this).find('.marginBox').droppable({
            hoverClass: "ui-state-hover",
            accept: function () { return true; },
            drop: function (event, ui) {
                //is it being dragged in from a toolbox, or just moved around inside the page?
                if ($(ui.draggable).hasClass('widgetInToolbox')) {

                    //review: since we already did a clone during the tearoff, why clone again?
                    var $x = $($(ui.draggable).clone()[0]);
                    // $x.text("");

                    //we need different behavior when it is in the toolbox vs. once it is live
                    $x.attr("class", $x.data("classesafterdrop"));
                    $x.removeAttr("classesafterdrop");

                    if ($x.hasClass('bloom-imageContainer')) {
                        SetupImageContainer($x);
                    }

                    //review: this find() implies that the draggable thing isn't necesarily the widgetInToolbox. Why not?
//                    $(this).find('.widgetInToolbox')
//                            .removeAttr("style")
//                            .draggable({ containment: "parent" })
//                            .removeClass("widgetInToolbox")
//                            .SetupResizableElement(this)
                    //                            .SetupDeletable(this);
                    $x.removeAttr("style");
                    $x.draggable({ containment: "parent" });
                    $x.removeClass("widgetInToolbox");
                    SetupResizableElement($x);
                    SetupDeletable($x);

                    $(this).append($x);
                }
            }
        });
        var lang1ISO = GetSettings().languageForNewTextBoxes;
        var heading1CenteredWidget = '<div class="heading1-style centered widgetInToolbox"  data-classesafterdrop="bloom-translationGroup heading1-style centered bloom-resizable bloom-deletable bloom-draggable"><div data-classesafterdrop="bloom-editable bloom-content1" lang="' + lang1ISO + '">Heading 1 Centered</div></div>';
        var heading2LeftWidget = '<div class="heading2-style widgetInToolbox"  data-classesafterdrop="bloom-translationGroup heading2-style  bloom-resizable bloom-deletable bloom-draggable"><div data-classesafterdrop="bloom-editable bloom-content1" lang="' + lang1ISO + '">Heading 2, Left</div></div>';
        var fieldWidget = '<div class="widgetInToolbox" data-classesafterdrop="bloom-translationGroup bloom-resizable bloom-deletable bloom-draggable"><div data-classesafterdrop="bloom-editable bloom-content1" lang="' + lang1ISO + '"> A block of normal text.</div></div>';
        // old one: var imageWidget = '<div class="bloom-imageContainer bloom-resizable bloom-draggable  bloom-deletable widgetInToolbox"><img src="placeHolder.png"></div>';
        var imageWidget = '<div class="widgetInToolbox " data-classesafterdrop="bloom-imageContainer  bloom-resizable bloom-draggable  bloom-deletable"><img src="placeHolder.png"></div>';

        var toolbox = $(this).parent().append("<div id='toolbox'><h3>Page Elements</h3><ul class='toolbox'><li>" + heading1CenteredWidget + "</li><li>" + heading2LeftWidget + "</li><li>" + fieldWidget + "</li><li>" + imageWidget + "</li></ul></div>");


        toolbox.find('.widgetInToolbox').each(function () {
            $(this).draggable({
                //note: this is just used for drawing what you drag around..
                //it isn't what the droppable is actually given. For that, look in the 'drop' item of the droppable() call above.
                helper: function(event) {
                    var tearOff = $(this).clone(); //.removeClass('widgetInToolbox');//by removing this, we show it with the actual size it will be when dropped
                    return tearOff;
                }
            });
        });
        $(this).qtipSecondary({
            content: "<div id='experimentNotice'><img src='file://" + GetSettings().bloomBrowserUIFolder + "/images/experiment.png'/>This is an experimental prototype of template-making within Bloom itself. Much more work is needed before it is ready for real work, so don't bother reporting problems with it yet. The Trello board is <a href='https://trello.com/board/bloom-custom-template-dev/4fb2501b34909fbe417a7b7d'>here</a></b></div>",
            show: { ready: true },
            hide: false,
            position: {
                at: 'right top',
                my: 'left top'
            },
            style: {
                classes: 'ui-tooltip-red',
                tip: { corner: false }
            }
        });
    })
}


function AddExperimentalNotice(element) {
    $(element).qtipSecondary({
        content: "<div id='experimentNotice'><img src='file://" + GetSettings().bloomBrowserUIFolder + "/images/experiment.png'/>This page is an experimental prototype which may have many problems, for which we apologize.<div/>"
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

 //Sets up the (currently green) qtip bubbles that give you the contents of the box in the source languages
function MakeSourceTextDivForGroup(group) {

    var divForBubble = $(group).clone();
    $(divForBubble).removeAttr('style');

    //make the source texts in the bubble read-only
    $(divForBubble).find("textarea, div").each(function() {
        $(this).attr("readonly", "readonly");
        $(this).removeClass('bloom-editable');
        $(this).attr("contenteditable", "false");
        // If we change font size, that should NOT affect the source text bubbles
        var styleClass = GetStyleClassFromElement(this);
        if (styleClass)
            $(this).removeClass(styleClass);
        $(this).attr('style', 'font-size: 1.2em; line-height: 1.2em;')
    });

    $(divForBubble).removeClass(); //remove them all
    $(divForBubble).addClass("ui-sourceTextsForBubble");
    //don't want the vernacular in the bubble
    $(divForBubble).find("*[lang='" + GetDictionary().vernacularLang + "']").each(function() {
        $(this).remove();
    });
    //don't want empty items in the bubble
    $(divForBubble).find("textarea:empty, div:hasNoText").each(function() {
        $(this).remove();
    });

    //don't want bilingual/trilingual boxes to be shown in the bubble
    $(divForBubble).find("*.bloom-content2, *.bloom-content3").each(function() {
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
        var dictionary = GetDictionary();
        var items = $(this).find("textarea, div");
        items.sort(function(a, b) {
            var keyA = $(a).attr('lang');
            var keyB = $(b).attr('lang');
            if (keyA == dictionary.vernacularLang)
                return -1;
            if (keyB == dictionary.vernacularLang)
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
            var languageName = dictionary[iso];
            if (!languageName)
                languageName = iso;
            var shouldShowOnPage = (iso == dictionary.vernacularLang)  /* could change that to 'bloom-content1' */ || $(this).hasClass('bloom-contentNational1') || $(this).hasClass('bloom-contentNational2') || $(this).hasClass('bloom-content2') || $(this).hasClass('bloom-content3');

            if(iso=== GetSettings().defaultSourceLanguage) {
                selectorOfDefaultTab = "li#" + iso; //selectorOfDefaultTab="li:#"+iso; this worked in jquery 1.4
            }
            // in translation mode, don't include the vernacular in the tabs, because the tabs are being moved to the bubble
            if (iso !== "z" && (shellEditingMode || !shouldShowOnPage)) {
                $(list).append('<li id="'+iso+'"><a class="sourceTextTab" href="#' + iso + '">' + languageName + '</a></li>');
            }
        });
    });

    //now turn that new div into a set of tabs
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

    // turn that tab thing into a bubble, and attach it to the original div ("group")
  $(group).each(function () {
      // var targetHeight = Math.max(55, $(this).height()); // This ensures we get at least one line of the source text!

      showEvents = false;
      hideEvents = false;
      shouldShowAlways = true;

        //todo: really, this should detect some made-up style, so that we can control this behavior via the stylesheet
        if($(this).hasClass('wordsDiv')) {
            showEvents = 'focusin';
            hideEvents = 'focusout';
            shouldShowAlways = false;
        }
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
          //events: {
          //    render: function (event, api) {
          //        api.elements.content.height(targetHeight);
          //    }
          //},
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


function GetLocalizedHint(whatToSay, targetElement) {
    if(whatToSay.startsWith("*")){
        whatToSay = whatToSay.substring(1,1000);
    }

    var dictionary = GetDictionary();

    if(whatToSay in dictionary) {
        whatToSay = dictionary[whatToSay];
    }

    //stick in the language
    for (key in dictionary) {
        if (key.startsWith("{"))
            whatToSay = whatToSay.replace(key, dictionary[key]);

        whatToSay = whatToSay.replace("{lang}", dictionary[$(targetElement).attr('lang')]);
    }
    return whatToSay;
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

//Bloom "imageContainer"s are <div>'s with wrap an <img>, and automatically proportionally resize
//the img to fit the available space
function SetupImageContainer(containerDiv) {
    $(containerDiv).mouseenter(function () {
        var buttonModifier = "largeImageButton";
        if ($(this).height() < 80) {
            buttonModifier = 'smallImageButton';
        }
        $(this).prepend("<button class='pasteImageButton " + buttonModifier + "' title='Paste Image'></button>");
        $(this).prepend("<button class='changeImageButton " + buttonModifier + "' title='Change Image'></button>");

        var img = $(this).find('img');
        if (CreditsAreRelevantForImage(img)) {
            $(this).prepend("<button class='editMetadataButton " + buttonModifier + "' title='Edit Image Credits, Copyright, & License'></button>");
        }

        $(this).addClass('hoverUp');
    })
    .mouseleave(function () {
        $(this).removeClass('hoverUp');
        $(this).find(".changeImageButton").each(function () {
            $(this).remove()
        });
        $(this).find(".pasteImageButton").each(function () {
            $(this).remove()
        });
        $(this).find(".editMetadataButton").each(function () {
            if (!$(this).hasClass('imgMetadataProblem')) {
                $(this).remove()
            }
        });
    });
}

function CreditsAreRelevantForImage(img) {
    return $(img).attr('src').toLowerCase().indexOf('placeholder') == -1; //don't offer to edit placeholder credits
}

//While the actual metada is embedded in the images (Bloom/palaso does that), Bloom sticks some metadata in data-* attributes
// so that we can easily & quickly get to the here.
function SetOverlayForImagesWithoutMetadata() {
    $(".bloom-imageContainer").each(function () {
        var img = $(this).find('img');
        if (!CreditsAreRelevantForImage(img)) {
           return;
        }
        var container = $(this);

        UpdateOverlay(container, img);

        //and if the bloom program changes these values (i.e. the user changes them using bloom), I
        //haven't figured out a way (appart from polling) to know that. So for now I'm using a hack
        //where Bloom calls click() on the image when it wants an update, and we detect that here.
        $(img).click(function () {
            UpdateOverlay(container, img);
        });
    });
}

function UpdateOverlay(container, img) {

    $(container).find(".imgMetadataProblem").each(function () {
        $(this).remove()
    });

    //review: should we also require copyright, illustrator, etc? In many contexts the id of the work-for-hire illustrator isn't available
    var copyright = $(img).attr('data-copyright');
    if (!copyright || copyright.length == 0) {

        var buttonModifier = "largeImageButton";
        if ($(container).height() < 80) {
            buttonModifier = 'smallImageButton';
        }

        $(container).prepend("<button class='editMetadataButton imgMetadataProblem "+buttonModifier+"' title='Image is missing information on Credits, Copyright, or License'></button>");
    }
}

// Instead of "missing", we want to show it in the right ui language. We also want the text
// to indicate that it might not be missing, just didn't load (this happens on slow machines)
// TODO: internationalize
function SetAlternateTextOnImages(element) {
    if ($(element).attr('src').length > 0) { //don't show this on the empty license image when we don't know the license yet
        $(element).attr('alt', 'This picture, ' + $(element).attr('src') + ', is missing or was loading too slowly.');
    }
    else {
        $(element).attr('alt', '');//don't be tempted to show something like a '?' unless you fix the result when you have a custom book license on top of that '?'
    }
}

function SetupResizableElement(element) {
    $(element).mouseenter(
        function () {
            $(this).addClass("ui-mouseOver")
        }).mouseleave(function () {
            $(this).removeClass("ui-mouseOver")
        });
    var childImgContainer = $(element).find(".bloom-imageContainer");
    // A Picture Dictionary Word-And-Image
    if ($(childImgContainer).length > 0) {
        /* The case here is that the thing with this class actually has an
         inner image, as is the case for the Picture Dictionary.
         The key, non-obvious, difficult requirement is keeping the text below
         a picture dictionary item centered underneath the image.  I'd be
         surprised if this wasn't possible in CSS, but I'm not expert enough.
         So, I switched from having the image container be resizable, to having the
         whole div (image+headwords) be resizable, then use the "alsoResize"
         parameter to make the imageContainer resize.  Then, in order to make
         the image resize in real-time as you're dragging, I use the "resize"
         event to scale the image up proportionally (and centered) inside the
         newly resized container.
         */
        var img = $(childImgContainer).find("img");
        $(element).resizable({handles:'nw, ne, sw, se',
            containment: "parent",
            alsoResize:childImgContainer,
           resize:function (event, ui) {
                img.scaleImage({scale:"fit"})
            }});
        return $(element);
    }
    //An Image Container div (which must have an inner <img>
    else if ($(element).hasClass('bloom-imageContainer')) {
        var img = $(element).find("img");
        $(element).resizable({handles:'nw, ne, sw, se',
            containment: "parent",
            resize:function (event, ui) {
                img.scaleImage({scale:"fit"})
            }});
    }
    // some other kind of resizable
    else {
        $(element).resizable({
            handles:'nw, ne, sw, se',
            containment: "parent",
             stop: ResizeUsingPercentages,
            start: function(e,ui){
               if($(ui.element).css('top')=='0px' && $(ui.element).css('left')=='0px'){
                   $(ui.element).data('doRestoreRelativePosition', 'true');
               }
            }
        });
    }
}

//jquery resizable normally uses pixels. This makes it use percentages, which are mor robust across page size/orientation changes
function ResizeUsingPercentages(e,ui){
    var parent = ui.element.parent();
    ui.element.css({
        width: ui.element.width()/parent.width()*100+"%",
        height: ui.element.height()/parent.height()*100+"%"
    });

    //after any resize jquery adds an absolute position, which we don't want unless the user has resized
    //so this removes it, unless we previously noted that the user had moved it
    if($(ui.element).data('doRestoreRelativePosition'))
    {
        ui.element.css({
            position: '',
            top: '',
            left: ''
        });
    }
    $(ui.element).removeData('hadPreviouslyBeenRelocated');
}

// Actual testable determination of overflow or not
jQuery.fn.IsOverflowing = function () {
    var element = $(this)[0];
    // We want to prevent an inner div from expanding past the borders set by any containing marginBox class.
    var marginBoxParent = $(element).parents('.marginBox');
    var parentBottom;
    if(marginBoxParent && marginBoxParent.length > 0)
        parentBottom = $(marginBoxParent[0]).offset().top + $(marginBoxParent[0]).outerHeight(true);
    else
        parentBottom = 999999;
    var elemTop = parseInt($(element).offset().top);
    var elemBottom = elemTop + $(element).outerHeight(false);
    // console.log("Offset top: " + elemTop + " Outer Height: " + $(element).outerHeight(false));
    // If css has "overflow: visible;", scrollHeight is always 2 greater than clientHeight.
    // This is because of the thin grey border on a focused input box.
    // In fact, the focused grey border causes the same problem in detecting the bottom of a marginBox
    // so we'll apply the same 'fudge' factor to both comparisons.
    var focusedBorderFudgeFactor = 2;

   //the "basic book" template has a "Just Text" page which does some weird things to get vertically-centered
   //text. I don't know why, but this makes the clientHeight 2 pixels larger than the scrollHeight once it
   //is beyond its minimum height. We can detect that we're using this because it has this "firefoxHeight" data
   //element.
   var growFromCenterVerticalFudgeFactor =0;
   if($(element).data('firefoxheight')){
    growFromCenterVerticalFudgeFactor = 2;
   }

   //in the Picture Dictionary template, all words have a scrollheight that is 3 greater than the client height.
   //In the Headers of the Term Intro of the SHRP C1 P3 Pupil's book, scrollHeight = clientHeight + 6!!! Sigh.
   // the focussedBorderFudgeFactor takes care of 2 pixels, this adds one more.
   var shortBoxFudgeFactor = 4;

  //console.log('s='+element.scrollHeight+' c='+element.clientHeight);

   return element.scrollHeight > element.clientHeight + focusedBorderFudgeFactor + growFromCenterVerticalFudgeFactor + shortBoxFudgeFactor ||
       element.scrollWidth > element.clientWidth + focusedBorderFudgeFactor ||
     elemBottom > parentBottom + focusedBorderFudgeFactor;
};

// When a div is overfull,
// we add the overflow class and it gets a red background or something
function AddOverflowHandler() {
  //NB: for some historical reason in March 2014 the calendar still uses textareas
    $("div.bloom-editable, textarea").on("keyup paste", function (e) {
        var $this = $(this);
        // Give the browser time to get the pasted text into the DOM first, before testing for overflow
        // GJM -- One place I read suggested that 0ms would work, it just needs to delay one 'cycle'.
        //        At first I was concerned that this might slow typing, but it doesn't seem to.
        setTimeout(function () {
            if ($this.IsOverflowing())
                $this.addClass('overflow');
            else {
                if ($this.hasClass('overflow'))
                    $this.removeClass('overflow');
            }
        }, 100); // 100 milliseconds
        e.stopPropagation();
    });
}

//---------------------------------------------------------------------------------

jQuery(document).ready(function () {
    if($.fn.qtip)
        $.fn.qtip.zindex = 15000;
    //gives an error $.fn.qtip.plugins.modal.zindex = 1000000 - 20;

    //add a marginBox if it's missing. We introduced it early in the first beta
    $(".bloom-page").each(function () {
        if ($(this).find(".marginBox").length == 0) {
            $(this).wrapInner("<div class='marginBox'></div>");
        }
    });

    AddToolbox();

    //make textarea edits go back into the dom (they were designed to be POST'ed via forms)
    jQuery("textarea").blur(function () {
        this.innerHTML = this.value;
    });


    jQuery.fn.reverse = function () {
         return this.pushStack(this.get().reverse(), arguments);
    };

    //if this browser doesn't have endsWith built in, add it
    if (typeof String.prototype.endsWith !== 'function') {
    String.prototype.endsWith = function (suffix) {
            return this.indexOf(suffix, this.length - suffix.length) !== -1;
        };
    }

    //firefox adds a <BR> when you press return, which is lame because you can't put css styles on BR, such as indent.
    //Eventually we may use a wysiwyg add-on which does this conversion as you type, but for now, we change it when
    //you tab or click out.
    jQuery(".bloom-editable").blur(function () {

        //This might mess some things up, so we're only applying it selectively
        if ($(this).closest('.bloom-requiresParagraphs').length == 0
           && ($(this).css('border-top-style') != 'dashed')) //this signal used to let the css add this conversion after some SIL-LEAD SHRP books were already typed
        return;

        var x = $(this).html();

        //the first time we see a field editing in Firefox, it won't have a p opener
        if (!x.startsWith('<p>')) {
            x = "<p>" + x;
        }

        x = x.split("<br>").join("</p><p>");

        //the first time we see a field editing in Firefox, it won't have a p closer
        if (!x.endsWith('</p>')) {
            x = x + "</p>";
        }
        $(this).html(x);

        //If somehow you get leading empty paragraphs, FF won't let you delete them
        $('p').each(function () {
            if ($(this).text() === "") {
                $(this).remove();
            } else {
                return false; //break
            }
        });

        //for some reason, perhaps FF-related, we end up with a new empty paragraph each time
        //so remove trailing <p></p>s
        $('p').reverse().each(function () {
            if ($(this).text() === "") {
                $(this).remove();
            } else {
                return false; //break
            }
        });
    });

    //when we discover an empty text box that has been marked to use paragraphs, start us off on the right foot
    $('.bloom-editable').focus(function () {
        if ($(this).closest('.bloom-requiresParagraphs').length == 0
            && ($(this).css('border-top-style') != 'dashed')) //this signal used to let the css add this conversion after some SIL-LEAD SHRP books were already typed
                return;

        if ($(this).text() == '') {
            //stick in a paragraph, which makes FF do paragraphs instead of BRs.
            $(this).html('<p>&nbsp;</p>'); // &zwnj; (zero width non-joiner) would be better but it makes the cursor invisible

            //now select that space, so we delete it when we start typing

            var el = $(this).find('p')[0].childNodes[0];
            var range = document.createRange();
            range.selectNodeContents(el);
            var sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }
        else {
            var el = $(this).find('p')[0];
            if (!el)
                return; // these have text, but not p's yet. We'll have to wait until they leave (blur) to add in the P's.
            var range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(true);//move to start of first paragraph
            var sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }
    //TODO if you do Ctrl+A and delete, you're now outside of our <p></p> zone. clicking out will trigger the blur handerl above, which will restore it.
    });

    SetBookCopyrightAndLicenseButtonVisibility();

    /*
    //when a textarea gets focus, send Bloom a dictionary of all the translations found within
    //the same parent element
    jQuery("textarea, div.bloom-editable").focus(function () {
    event = document.createEvent('MessageEvent');
    var origin = window.location.protocol + '//' + window.location.host;
    var obj = {};
    $(this).parent().find("textarea, div.bloom-editable").each(function () {
    obj[$(this).attr("lang")] = $(this).text();
    })
    var json = obj; //.get();
    json = JSON.stringify(json);
    event.initMessageEvent('textGroupFocused', true, true, json, origin, 1234, window, null);
    document.dispatchEvent(event);
    });
    */

    //in bilingual/trilingual situation, re-order the boxes to match the content languages, so that stylesheets don't have to
    $(".bloom-translationGroup").each(function () {
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
    $("div.bloom-editable").on("paste", function (e) {
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

    // Add overflow event handlers so that when a div is overfull,
    // we add the overflow class and it gets a red background or something
    AddOverflowHandler();

    //Make F8 apply a superscript style (later we'll change to ctrl+shift+plus, as word does. But capturing those in js by hand is a pain.
    //nb: we're avoiding ctrl+plus and ctrl+shift+plus (as used by MS Word), because they means zoom in browser. also three keys is too much
    $("div.bloom-editable").on('keydown', null, 'F6', function (e) {
        var selection = document.getSelection();
        if (selection != null && selection != '') {
            //NB: by using exeCommand, we get undo-ability
            document.execCommand("insertHTML", false, "<span class='superscript'>" + document.getSelection() + "</span>");
        }
    });
    $("div.bloom-editable").on('keydown', null, 'F7', function (e) {
        e.preventDefault();
        document.execCommand("formatBlock", false, "H1");
    });

    $("div.bloom-editable").on('keydown', null, 'F8', function (e) {
        e.preventDefault();
        document.execCommand("formatBlock", false, "H2");
    });
//    jQuery("div.bloom-editable").on('keydown', null, 'ctrl+shift+c', function (e) {
//        e.preventDefault();
//        document.execCommand("justifyCenter", false, null);
//    });

    //there doesn't appear to be a good simple way to clear out formatting
    jQuery("div.bloom-editable").on('keydown', null, 'ctrl+space', function (e) {
        e.preventDefault();
        document.execCommand("removeFormat", false, false);//will remove bold, italics, etc. but not things that use elements, like h1
        //TODO now for elements (h1, span, etc), we could do a regex and remove them. The following is just a temporary bandaid
        //Recommended, but didn't work: document.execCommand("formatBlock", false, 'div');
    });
    //--------------------------------
    //keep divs vertically centered (yes, I first tried *all* the css approaches, they don't work for our situation)

    //do it initially
    $(".bloom-centerVertically").CenterVerticallyInParent();
    //reposition as needed
    $(".bloom-centerVertically").resize(function () { //nb: this uses a 3rd party resize extension from Ben Alman; the built in jquery resize only fires on the window
        $(this).CenterVerticallyInParent();
    });

    /* Defines a starts-with function*/
    if (typeof String.prototype.startsWith != 'function') {
        String.prototype.startsWith = function (str) {
            return this.indexOf(str) == 0;
        };
    }

    //Add little language tags
    $("div.bloom-editable:visible").each(function () {
        var key = $(this).attr("lang");
        var dictionary = GetDictionary();
        var whatToSay = dictionary[key];
        if (whatToSay == null)
            whatToSay = key; //just show the code

        if (key == "*")
            return; //seeing a "*" was confusing even to me

        //with a really small box that also had a hint qtip, there wasn't enough room and the two fough with each other, leading to flashing back and forth
        if ($(this).width() < 100) {
            return;
        }

        // if this or any parent element has the class bloom-hideLanguageNameDisplay, we don't want to show any of these tags
        // first usage (for instance) was turning off language tags for a whole page
        if ($(this).hasClass('bloom-hideLanguageNameDisplay') || $(this).parents('.bloom-hideLanguageNameDisplay').length != 0) {
            return;
        }

        //TODO: I haven't been able to get these to work right... I just want the tooltip to hide when the user is in the box at the moment,
        //then reappear

        var shouldShowAlways = true; // "mouseleave unfocus";
        var hideEvents = false; // "mouseover focusin";

        //             shouldShowAlways = false;
        //           hideEvents = 'unfocus mouseleave';

        $(this).qtip({
            content: whatToSay,

            position: {
                my: 'top right',
                at: 'bottom right',
                adjust: { y: -25 }
            },
            show: { ready: shouldShowAlways },
            hide: {
                event: hideEvents
            },
            style: {
                classes: 'ui-languageToolTip',
                tip: {
                  border :0
                }
            }
        });
        // doing this makes it imposible to reposition them           .removeData('qtip'); // allows multiple tooltips. See http://craigsworks.com/projects/qtip2/tutorials/advanced/
    });

    // I took away this feature becuase qtip was changing titles to "oldtitle" which caused problems because we save the result. So now, we just
    // say that if you want a momentary qtip, do a data-hint and start it with '*'   //Add popup yellow bubbles to match title attributes
    //    $("*[title]").each(function() {
    //        $(this).qtip({ position: {
    //                at: 'right bottom', //I like this, but it doesn't reposition well -->at: 'right center',
    //                my: 'top left', //I like this, but it doesn't reposition well-->  my: 'left center',
    //                viewport: $(window)
    //            },
    //            style: { classes:'ui-tooltip-shadow ui-tooltip-plain' } });
    //    });

    //Handle <label>-defined hint bubbles on mono fields, that is divs that aren't in the context of a
    //bloom-translationGroup (those should have a single <label> for the whole group).
    //Notice that the <label> inside an editable div is in a precarious position, it could get
    //edited away by the user. So we are moving the contents into a data-hint attribute on the field.
    //Yes, it could have been placed there in the 1st place, but the <label> approach is highly readable,
    //so it is preferred when making new templates by hand.
    $("*.bloom-editable label.bubble").each(function () {
        var labelElement = $(this);
        var whatToSay = labelElement.text();
        var onFocusOnly = labelElement.hasClass('bloom-showOnlyWhenTargetHasFocus');

        var enclosingEditableDiv = labelElement.parent();
        enclosingEditableDiv.attr('data-hint', labelElement.text());
        labelElement.remove();

        //attach the bubble, this editable only, then remove it
        MakeHelpBubble($(enclosingEditableDiv), labelElement, whatToSay, onFocusOnly);
    });

    //<label class='bubble'> inside a div.bloom-translationGroup to gives a hint bubble outside each of
    // the fields, with some template-filing and localization for each.
    // Note that Version 1.0, we didn't have this <label> ability but we had @data-hint.
    //Using <label> instead of the attribute makes the html much easer to read, write, and add additional
    //behaviors through classes
    $("*.bloom-translationGroup > label.bubble").each(function () {
        var labelElement = $(this);
        var whatToSay = labelElement.text();
        var onFocusOnly = labelElement.hasClass('bloom-showOnlyWhenTargetHasFocus');

        //attach the bubble, separately, to every field inside the group
        labelElement.parent().find("div").each(function () {
            var onFocusOnly = labelElement.hasClass('bloom-showOnlyWhenTargetHasFocus');
            MakeHelpBubble($(this), labelElement, whatToSay, onFocusOnly);
        });
    });

    $("*.bloom-imageContainer > label.bubble").each(function () {
        var labelElement = $(this);
        var imageContainer = $(this).parent();
        var whatToSay = labelElement.text();
        var onFocusOnly = labelElement.hasClass('bloom-showOnlyWhenTargetHasFocus');
        MakeHelpBubble(imageContainer, labelElement, whatToSay, onFocusOnly);
    });

    //html5 provides for a placeholder attribute, but not for contenteditable divs like we use.
    //So one of our foundational stylesheets looks for @data-placeholder and simulates the
    //@placeholder behavior.
    //Now, what's going on here is that we also support
    //<label class='placeholder'> inside a div.bloom-translationGroup to get this placeholder
    //behavior on each of the fields inside the group .
    //Using <label> instead of the attribute makes the html much easer to read, write, and add additional
    //behaviors through classes.
    //So the job of this bit here is to take the label.bubble and create the data-placeholders.
    $("*.bloom-translationGroup > label.placeholder").each(function () {

        var labelText = $(this).text();

        //put the attributes on the individual child divs
        $(this).parent().find('.bloom-editable').each(function () {

            //enhance: it would make sense to allow each of these to be customized for their div
            //so that you could have a placeholder that said "Name in {lang}", for example.
            $(this).attr('data-placeholder', labelText);
            //next, it's up to CSS to draw the placeholder when the field is empty.
        });
    });

    //This is the "low-level" way to get a hint bubble, cramming it all into a data-hint attribute.
    //It is used by the "high-level" way in the monolingual case where we don't have a bloom-translationGroup,
    //and need a place to preserve the contents of the <label>, which is in danger of being edited away.
    $("*[data-hint]").each(function () {
        var whatToSay = $(this).attr("data-hint");//don't use .data(), as that will trip over any } in the hint and try to interpret it as json
        if (!whatToSay || whatToSay.length == 0)
            return;

        //make hints that start with a * only show when the field has focus
        var showOnFocusOnly = whatToSay.startsWith("*");

        if (whatToSay.startsWith("*")) {
            whatToSay = whatToSay.substring(1, 1000);
        }

        MakeHelpBubble($(this), $(this), whatToSay, showOnFocusOnly);
    });

    $.fn.hasAttr = function (name) {
        var attr = $(this).attr(name);

        // For some browsers, `attr` is undefined; for others,
        // `attr` is false.  Check for both.
        return (typeof attr !== 'undefined' && attr !== false);
    };

    //Show data on fields
    /* disabled to see if we can do fine without it
    $("*[data-book], *[data-library], *[lang]").each(function() {

    var data = " ";
    if ($(this).hasAttr("data-book")) {
    data = $(this).attr("data-book");
    }
    if ($(this).hasAttr("data-library")) {
    data = $(this).attr("data-library");
    }
    $(this).qtipSecondary({
    content: {text: $(this).attr("lang") + "<br>" + data}, //, title: { text:  $(this).attr("lang")}},

    position: {
    my: 'top right',
    at: 'top left'
    },
    //                  show: {
    //                                  event: false, // Don't specify a show event...
    //                                  ready: true // ... but show the tooltip when ready
    //                              },
    //                  hide:false,//{     fixed: true },// Make it fixed so it can be hovered over    },
    style: {'default': false,
    tip: {corner: false,border: false},
    classes: 'fieldInfo-qtip'
    }
    });
    });
    */

    //eventually we want to run this *after* we've used the page, but for now, it is useful to clean up stuff from last time
    Cleanup();

    //make images look click-able when you cover over them
    jQuery(".bloom-imageContainer").each(function () {
        SetupImageContainer(this);
    });

    //todo: this had problems. Check out the later approach, seen in draggableLabel (e.g. move handle on the inside, using a background image on a div)
    jQuery(".bloom-draggable").mouseenter(function () {
        $(this).prepend("<button class='moveButton' title='Move'></button>");
        $(this).find(".moveButton").mousedown(function (e) {
            $(this).parent().trigger(e);
        });
    });
    jQuery(".bloom-draggable").mouseleave(function () {
        $(this).find(".moveButton").each(function () {
            $(this).remove()
        });
    });

    $('div.bloom-editable').each(function () {
        $(this).attr('contentEditable', 'true');
    });

    // Bloom needs to make some fields readonly. E.g., the original license when the user is translating a shellbook
    // Normally, we'd control this is a style in editTranslationMode.css/editOriginalMode.css. However, "readonly" isn't a style, just
    // an attribute, so it can't be included in css.
    // The solution here is to add the readonly attribute when we detect that the css has set the cursor to "not-allowed".
    $('textarea, div').focus(function () {
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


    // If the user moves over something they can't edit, show a tooltip explaining why not
    $('*[data-hint]').each(function () {

        if ($(this).css('cursor') == 'not-allowed') {
            var whyDisabled = "You cannot change these because this is not the original copy.";
            if ($(this).hasClass('bloom-readOnlyInEditMode')) {
                whyDisabled = "You cannot put anything in there while making an original book.";
            }

            var whatToSay = $(this).attr("data-hint");//don't use .data(), as that will trip over any } in the hint and try to interpret it as json

            whatToSay = GetLocalizedHint(whatToSay,$(this)) + " <br/>" + whyDisabled;
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
    $('div.bloom-readOnlyInTranslationMode').focus(function () {
        if ($(this).css('cursor') == 'not-allowed') {
            $(this).removeAttr("contentEditable");
        }
        else {
            $(this).attr("contentEditable", "true");
        }
    });

    // this is gone because of a memory violation bug in geckofx 11 with messaging. Now we just notice the click from within c#
    //    // Send all the data from this div in a message, so Bloom can do something like show a custom dialog box
    // for editing the data. We only notice the click if the cursor style is 'pointer', so that CSS can turn this on/off.
    //    $('div.bloom-metaData').each(function() {
    //        if ($(this).css('cursor') == 'pointer') {
    //            $(this).click(function() {
    //                event = document.createEvent('MessageEvent');
    //                var origin = window.location.protocol + '//' + window.location.host;
    //                var obj = {};
    //                $(this).find("*[data-book]").each(function() {
    //                    obj[$(this).attr("data-book")] = $(this).text();
    //                })
    //                var json = obj; //.get();
    //                json = JSON.stringify(json);
    //                event.initMessageEvent('divClicked', true, true, json, origin, "", window, null);
    //                document.dispatchEvent(event);
    //            })
    //        }
    //    });

    //first used in the Uganda SHRP Primer 1 template, on the image on day 1
    //This took *enormous* fussing in the css. TODO: copy what we learned there
    //to the (currently experimental) Toolbox template (see 'bloom-draggable')
    $(".bloom-draggableLabel")
        .draggable(
        {
            containment: "bloom-imageContainer"
           ,handle: '.dragHandle'
        })
       .mouseenter(function () {
        $(this).prepend(" <div class='dragHandle'></div>")
        });

        jQuery(".bloom-draggableLabel").mouseleave(function () {
            $(this).find(".dragHandle").each(function () {
                $(this).remove()
            })
        });

    // add drag and resize ability where elements call for it
    //   $(".bloom-draggable").draggable({containment: "parent"});
    $(".bloom-draggable").draggable({ containment: "parent",
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
    $(".bloom-classSwitchingCombobox").each(function(){
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
    $(".bloom-classSwitchingCombobox").change(function(){
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
    $("DIV.bloom-page.bloom-enablePageCustomization DIV.bloom-deletable").each(function () {
        SetupDeletable(this);
    });

    $(".pictureDictionaryPage").each(function () {
        AddExperimentalNotice(this);
    });

    $(".bloom-resizable").each(function () {
        SetupResizableElement(this);
    });

    $("img").each(function () {
        SetAlternateTextOnImages(this);
    });

    SetOverlayForImagesWithoutMetadata();

    //focus on the first editable field

    SetupShowingTopicChooserWhenTopicIsClicked();

    //copy source texts out to their own div, where we can make a bubble with tabs out of them
    //We do this because if we made a bubble out of the div, that would suck up the vernacular editable area, too, and then we couldn't translate the book.
    $("*.bloom-translationGroup").each(function () {
        if ($(this).find("textarea, div").length > 1) {
            MakeSourceTextDivForGroup(this);
        }
    });

    //make images scale up to their container without distorting their proportions, while being centered within it.
    $(".bloom-imageContainer img").scaleImage({ scale: "fit" }); //uses jquery.myimgscale.js

    // when the image changes, we need to scale again:
    $(".bloom-imageContainer img").load(function () {
        $(this).scaleImage({ scale: "fit" });
    });

    //and when their parent is resized by the user, we need to scale again:
    $(".bloom-imageContainer img").each(function () {
        $(this).parent().resize(function () {
            $(this).find("img").scaleImage({ scale: "fit" });
            try {
                ResetRememberedSize(this);
            } catch (error) {
                console.log(error);
            }
        });
    });

    var editor = new StyleEditor('file://' + GetSettings().bloomBrowserUIFolder + "/bookEdit");

    $("div.bloom-editable:visible").each(function () {

        $(this).focus(function() {
           editor.AttachToBox(this);
        });
        //no: this removes the button just when we're clickin on one of the toolbar items
        //$(this).focusout(function () {
        //    editor.DetachFromBox(this);
        //});
    });

    //focus on the first editable field
    //$(':input:enabled:visible:first').focus();
    $("textarea, div.bloom-editable").first().focus(); //review: this might choose a textarea which appears after the div. Could we sort on the tab order?

    //editor.AddStyleEditBoxes('file://' + GetSettings().bloomBrowserUIFolder+"/bookEdit");
});

//function SetCopyrightAndLicense(data) {
//    $('*[data-book="copyright"]').each(function(){
//        $(this).text(data.copyright);}
//    )
function SetCopyrightAndLicense(data) {
    //nb: for textarea, we need val(). But for div, it would be text()
    $("DIV[data-book='copyright']").text(data.copyright);
    $("DIV[data-book='licenseUrl']").text(data.licenseUrl);
    $("DIV[data-book='licenseDescription']").text(data.licenseDescription);
    $("DIV[data-book='licenseNotes']").text(DecodeHtml(data.licenseNotes));
    var licenseImageValue = data.licenseImage + "?" + new Date().getTime(); //the time thing makes the browser reload it even if it's the same name
    if (data.licenseImage.length == 0) {
        licenseImageValue = ""; //don't wan the date on there
        $("IMG[data-book='licenseImage']").attr('alt', '');
    }

    $("IMG[data-book='licenseImage']").attr("src", licenseImageValue);
    SetBookCopyrightAndLicenseButtonVisibility();
}

function DecodeHtml(encodedString) {
    return encodedString.replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&#39;/g, "'");
}

function SetBookCopyrightAndLicenseButtonVisibility() {
    var shouldShowButton = !($("DIV.copyright").text());
    $("button#editCopyrightAndLicense").css("display", shouldShowButton ? "inline" : "none");
}

function FindOrCreateTopicDialogDiv() {
    var dialogContents = $("body").find("div#topicChooser");
    if (!dialogContents.length) {
        //$(temp).load(url);//this didn't work in bloom (it did in my browser, but it was FFver 9 wen Bloom was 8. Or the FF has the cross-domain security loosened perhaps?
        dialogContents = $("<div id='topicChooser' title='Topics'/>").appendTo($("body"));

        var topics = JSON.parse(GetSettings().topics);
        // var topics = ["Agriculture", "Animal Stories", "Business", "Culture", "Community Living", "Dictionary", "Environment", "Fiction", "Health", "How To", "Math", "Non Fiction", "Spiritual", "Personal Development", "Primer", "Science", "Tradition"];

        dialogContents.append("<ol id='topics'></ol>");
        for (i in topics) {
            $("ol#topics").append("<li class='ui-widget-content'>" + topics[i] + "</li>");
        }

        $("#topics").selectable();

        //This weird stuff is to make up for the jquery uI not automatically theme-ing... without the following, when you select an item, nothing visible happens (from stackoverflow)
        $("#topics").selectable({
            unselected: function() {
                $(":not(.ui-selected)", this).each(function() {
                    $(this).removeClass('ui-state-highlight');
                });
            },
            selected: function() {
                $(".ui-selected", this).each(function() {
                    $(this).addClass('ui-state-highlight');
                });
            }
        });
        $("#topics li").hover(
        function() {
            $(this).addClass('ui-state-hover');
        },
        function() {
            $(this).removeClass('ui-state-hover');
        });
    }
    return dialogContents;
}

//note, the normal way is for the user to click the link on the qtip.
//But clicking on the exiting topic may be natural too, and this prevents
//them from editing it by hand.
function SetupShowingTopicChooserWhenTopicIsClicked() {
    $("div[data-book='topic']").click(function () {
        if ($(this).css('cursor') == 'not-allowed')
            return;
        ShowTopicChooser();
    });
}

// This is called directly from Bloom via RunJavaScript()
function ShowTopicChooser() {
    var dialogContents = FindOrCreateTopicDialogDiv();
    var dlg = $(dialogContents).dialog({
        autoOpen: "true",
        modal: "true",
        //zIndex removed in newer jquery, now we get it in the css
        buttons: {
            "OK": function () {
                var t = $("ol#topics li.ui-selected");
                if (t.length) {
                    $("div[data-book='topic']").filter("[class~='bloom-contentNational1']").text(t[0].innerHTML);
                }
                $(this).dialog("close");
            }
        }
    });

    //make a double click on an item close the dialog
    dlg.find("li").dblclick(function () {
        var x = dlg.dialog("option", "buttons");
        x['OK'].apply(dlg);
    });
}
