 // VERTICALLY ALIGN FUNCTION
$.fn.VAlign = function() {
    return this.each(function(i) {
        var ah = $(this).height();
        var ph = $(this).parent().height();
        var mh = Math.ceil((ph - ah) / 2);
        $(this).css('margin-top', mh);
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

function Cleanup() {

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
    $("*").removeAttr("data-easytabs");
    
    $("div.ui-resizable-handle").remove();
    $('div, figure').each(function() {
        $(this).removeClass('ui-draggable');
        $(this).removeClass('ui-resizable');
    });


	$('div.bloom-editable').each( function() {
		TrimTrailingLineBreaksInDivs(this);
	});
}

function MakeSourceTextDivForGroup(group) {
    
    var divForBubble = $(group).clone();

    //make the source texts in the bubble read-only
    $(divForBubble).find("textarea, div").each(function() {
        $(this).attr("readonly", "readonly");
        $(this).removeClass('bloom-editable');
        $(this).attr("contenteditable", "false");
    });
    
    $(divForBubble).removeClass(); //remove them all
    $(divForBubble).addClass("ui-sourceTextsForBubble");
    //don't want the vernacular in the bubble
    $(divForBubble).find("*[lang='" + GetDictionary().vernacularLang + "']").each(function() {
        $(this).remove();
    });
    //don't want empty items in the bubble
    $(divForBubble).find("textarea:empty, div:empty").each(function() {
        $(this).remove();
    });

    //don't want bilingual/trilingual boxes to be shown in the bubble
    $(divForBubble).find("*.bloom-content2, *.bloom-content3").each(function() {
        $(this).remove();
    });

    //if there are no languages to show in the bubble, bail out now
    if ($(divForBubble).find("textarea, div").length == 0)
        return;
    
    $(this).after(divForBubble);

    var selectorOfDefaultTab="li:first-child";

    //make the li's for the source text elements in this new div, which will later move to a tabbed bubble
    $(divForBubble).each(function() {
        $(this).prepend('<ul class="editTimeOnly z"></ul>');
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

            if(iso=== dictionary.defaultSourceLanguage) {
                selectorOfDefaultTab="li:#"+iso;
            }
            // in translation mode, don't include the vernacular in the tabs, because the tabs are being moved to the bubble
            if (shellEditingMode || !shouldShowOnPage) {
                $(list).append('<li id="'+iso+'"><a class="sourceTextTab" href="#' + iso + '">' + languageName + '</a></li>');
            }
        });
    });

    //now turn that new div into a set of tabs
    if ($(divForBubble).find("li").length > 0) {
        $(divForBubble).easytabs({
            animate: false,
            defaultTab: selectorOfDefaultTab
        })
//        $(divForBubble).bind('easytabs:after', function(event, tab, panel, settings){
//            alert(panel.selector)
//        });

	}
	else {
		$(divForBubble).remove();//no tabs, so hide the bubble
		return;
	}
		
    


    // turn that tab thing into a bubble, and attach it to the original div ("group")
    $(group).each(function() {
        var targetHeight = $(this).height();
        
        $(this).qtip({
            position: {at: 'right center',
                my: 'left center',
                adjust: {
                    x: 10,
                    y: 0
                }
            },
            content: $(divForBubble),
            
            show: {
                ready: true // ... but show the tooltip when ready
            },
            events: {
                render: function(event, api) {
                    api.elements.content.height(targetHeight);
                }
            },
            style: {
                //doesn't work: tip:{ size: {height: 50, width:50}             },
                //doesn't work: tip:{ size: {x: 50, y:50}             },
                classes: 'ui-tooltip-green ui-tooltip-rounded uibloomSourceTextsBubble'},
            hide: false //{ when: 'mouseout', fixed: true }
        });
    });
}


 function GetLocalizedHint(element) {
     var whatToSay = $(element).data("hint");
     var dictionary = GetDictionary();
     for (key in dictionary) {
         if (key.startsWith("{"))
             whatToSay = whatToSay.replace(key, dictionary[key]);

         whatToSay = whatToSay.replace("{lang}", dictionary[$(element).attr('lang')]);
     }
     return whatToSay;
 }
 jQuery(document).ready(function() {



    //make textarea edits go back into the dom (they were designed to be POST'ed via forms)
    jQuery("textarea").blur(function() {
        this.innerHTML = this.value;
    });
    
    SetCopyrightAndLicenseButtonVisibility();

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
    $(".bloom-translationGroup").each(function() {
        var contentElements = $(this).find("textarea, div");
        contentElements.sort(function(a, b) {
			//using negatives so that something with none of these labels ends up with a > score and at the end
            var scoreA = $(a).hasClass('bloom-content1')*-3 + ($(a).hasClass('bloom-content2') * -2) + ($(a).hasClass('bloom-content3') * -1);
            var scoreB = $(b).hasClass('bloom-content1')*-3 + ($(b).hasClass('bloom-content2') * -2) + ($(b).hasClass('bloom-content3') * -1);
            if (scoreA < scoreB)
                return -1;
            if (scoreA > scoreB)
                return 1;
            return 0;
        });
        //do the actual rearrangement
        $(this).append(contentElements);
    });


    //when a textarea or div is overfull, add the overflow class so that it gets a red background or something
    //NB: we would like to run this even when there is a mouse paste, but currently don't know how
    //to get that event. You'd think change() would do it, but it doesn't. http://stackoverflow.com/questions/3035633/jquery-change-not-working-incase-of-dynamic-value-change
//    jQuery("textarea").keypress(function() {
//        var overflowing = this.scrollHeight > this.clientHeight;
//        if ($(this).hasClass('overflow') && !overflowing) {
//            $(this).removeClass('overflow');
//        }
//        else if (overflowing) {
//            $(this).addClass('overflow');
//        }
//    });
//    jQuery("div.bloom-editable").keypress(function() {
//        var overflowing = this.scrollHeight > this.clientHeight || this.scrollHieght > $(this).maxSize().height;
//        if ($(this).hasClass('overflow') && !overflowing) {
//            $(this).removeClass('overflow');
//        }
//        else if (overflowing) {
//            $(this).addClass('overflow');
//        }
//    });


    //--------------------------------
    //keep divs vertically centered (yes, I first tried *all* the css approaches, they don't work for our situation)

    //do it initially
    $(".bloom-verticalAlign").VAlign();
    //reposition as needed
    $(".bloom-verticalAlign").resize(function() { //nb: this uses a 3rd party resize extension from Ben Alman; the built in jquery resize only fires on the window
        $(this).VAlign();
    });

    /* Defines a starts-with function*/
    if (typeof String.prototype.startsWith != 'function') {
        String.prototype.startsWith = function(str) {
            return this.indexOf(str) == 0;
        };
    }

    $("*[title]").qtip({ position: {at: 'right center',
                my: 'left center'
            },
            style: { classes:'ui-tooltip-shadow ui-tooltip-plain' } });

    //put hint bubbles next to elements which call for them.
	//show those bubbles if the item is empty, or if it's not empty, then if it is in focus OR the mouse is over the item
    $("*[data-hint]").each(function() {
		
        if ($(this).css('border-bottom-color') == 'transparent') {
            return; //don't put tips if they can't edit it. That's just confusing
        }
        if ($(this).css('display') == 'none') {
            return; //don't put tips if they can't see it.
        }
        theClasses = 'ui-tooltip-shadow ui-tooltip-plain';
        pos = {
//            at: 'right center',
            my: 'left center',
            viewport: $(window)
        };
        var whatToSay = GetLocalizedHint(this);
        var shouldShowAlways = $(this).is(':empty');//if it was empty when we drew the page, keep the tooltip there
		var hideEvents = shouldShowAlways ? null : "focusout mouseleave";
        $(this).qtip({
            content: whatToSay,
            position: pos,
            show: {
                event: " focusin mouseenter",
                ready:  shouldShowAlways //would rather have this kind of dynamic thing, but it isn't right: function(){$(this).is(':empty')}//
            },
            hide: {
				event:hideEvents
			},
            adjust: { method: "flip shift"},
            style: {
                classes: theClasses
            },
            adjust:{screen:true, resize:true}
        });
    });
    
    $.fn.hasAttr = function(name) {
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
    jQuery(".bloom-imageContainer").mouseenter(function() {
//        var pasteButtonClass = 'pasteImageButton';
//        var chooseButtonClass = 'changeImageButton';
        var buttonModifier="largeImageButton";
        if($(this).height() < 80) {
            buttonModifier = 'smallImageButton';
        }
        $(this).prepend("<button class='pasteImageButton "+buttonModifier+"' title='Paste Image'></button>");
        $(this).prepend("<button class='changeImageButton "+buttonModifier+"' title='Change Image'></button>");
        $(this).addClass('hoverUp');
    }).mouseleave(function() {
        $(this).removeClass('hoverUp');
        $(this).find(".changeImageButton").each(function() {
            $(this).remove()
        });
        $(this).find(".pasteImageButton").each(function() {
            $(this).remove()
            });
    });
    
    jQuery(".bloom-draggable").mouseenter(function() {
        $(this).prepend("<button class='moveButton' title='Move'></button>");
        $(this).find(".moveButton").mousedown(function(e) {
            $(this).parent().trigger(e);
        });
    });
    jQuery(".bloom-draggable").mouseleave(function() {
        $(this).find(".moveButton").each(function() {
            $(this).remove()
        });
    });

    $('div.bloom-editable').each(function() {
        $(this).attr('contentEditable', 'true');
    });
    // Bloom needs to make some fields readonly. E.g., the original license when the user is translating a shellbook
    // Normally, we'd control this is a style in editTranslationMode.css/editOriginalMode.css. However, "readonly" isn't a style, just
    // an attribute, so it can't be included in css.
    // The solution here is to add the readonly attribute when we detect that the css has set the cursor to "not-allowed".
    $('textarea, div').focus(function() {
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
    $('*[data-hint]').each(function() {
            if ($(this).css('cursor') == 'not-allowed') {
                var whyDisabled = "You cannot change these because this is not the original copy.";
                if($(this).hasClass('bloom-readOnlyInEditMode')){
                    whyDisabled = "You cannot put anything in there while making an original.";
                }

                var whatToSay = GetLocalizedHint(this)+" <br/>"+whyDisabled;
                var theClasses = 'ui-tooltip-shadow ui-tooltip-red';
                var pos = {at: 'right center',
                            my: 'left center'
                        };
                $(this).qtip({
                            content: whatToSay,
                            position: pos,
                            show: {
                                event: " focusin mouseenter"
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
    $('div.bloom-readOnlyInTranslationMode').focus(function() {
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


    //add drag and resize ability where elements call for it
 //   $(".bloom-draggable").draggable({containment: "parent"});
    $(".bloom-draggable").draggable({containment: "parent",
            handle: '.bloom-imageContainer' });//without this "handle" restriction, clicks on the text boxes don't work. NB: ".moveButton" is really what we wanted, but didn't work, probably because the button is only created on the mouseEnter event, and maybe that's too late.

    $(".bloom-resizable").each(function() {
        var imgContainer = $(this).find(".bloom-imageContainer");
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
        if(imgContainer != null)        {
            var img = $(this).find(".bloom-imageContainer img");
            $(this).resizable({handles: 'nw, ne, sw, se', alsoResize: imgContainer,
            resize: function(event,ui){img.scaleImage({scale: "fit"})}});
        }
        /* just a normal image within a <div class='imageContainer' */
        else {
            $(".bloom-resizable").resizable({handles: 'nw, ne, sw, se'});
        }

    });

    $(".bloom-resizable").mouseenter(function() {
        $(this).addClass("ui-mouseOver")
    }).mouseleave(function() {
        $(this).removeClass("ui-mouseOver")
    });

    //focus on the first editable field
    //$(':input:enabled:visible:first').focus();
    $("textarea, div.bloom-editable").first().focus(); //review: this might choose a textarea which appears after the div. Could we sort on the tab order?
    
    SetupTopicDialog();

    //copy source texts out to their own div, where we can make a bubble with tabs out of them
    //We do this because if we made a bubble out of the div, that would suck up the vernacular editable area, too, and then we couldn't translate the book.
    $("*.bloom-translationGroup").each(function() {
        if ($(this).find("textarea, div").length > 1) {
            MakeSourceTextDivForGroup(this);
        }
    });


    //make images scale up to their container without distorting their proportions, while being centered within it.
    $(".bloom-imageContainer img").scaleImage({scale: "fit"}); //uses jquery.myimgscale.js

    // when the image changes, we need to scale again:
    $(".bloom-imageContainer img").load(function() {
        $(this).scaleImage({scale: "fit"});
    });

    //and when their parent is resized by the user, we need to scale again:
    $(".bloom-imageContainer img").each(function() {
        $(this).parent().resize(function() {
            $(this).find("img").scaleImage({scale: "fit"});
            ResetRememberedSize(this);
        });
    });
});

//function SetCopyrightAndLicense(data) {
//    $('*[data-book="copyright"]').each(function(){
//        $(this).text(data.copyright);}
//    )
function SetCopyrightAndLicense(data) {
    //nb: for textarea, we need val(). But for div, it would be text()
    $("DIV[data-book='copyright']").text(data.copyright);
    $("DIV[data-book='licenseUrl']").text(data.licenseUrl);
    $("DIV.licenseDescription").text(data.licenseDescription);
    $("DIV.licenseNotes").text(data.licenseNotes);
    $("IMG[data-book='licenseImage']").attr("src", data.licenseImage + "?" + new Date().getTime()); //the time thing makes the browser reload it even if it's the same name
    SetCopyrightAndLicenseButtonVisibility();
}

function SetCopyrightAndLicenseButtonVisibility() {
    var shouldShowButton = !($("DIV.copyright").text());
    $("button#editCopyrightAndLicense").css("display", shouldShowButton ? "inline" : "none");
}

function FindOrCreateTopicDialogDiv() {
    var dialogContents = $("body").find("div#topicChooser");
    if (!dialogContents.length) {
        //$(temp).load(url);//this didn't work in bloom (it did in my browser, but it was FFver 9 wen Bloom was 8. Or the FF has the cross-domain security loosened perhaps?
        dialogContents = $("<div id='topicChooser' title='Topics'/>").appendTo($("body"));
        var topics = ["Agriculture", "Animal Stories", "Business", "Culture", "Community Living", "Dictionary", "Environment", "Fiction", "Health", "How To", "Math", "Non Fiction", "Spiritual", "Personal Development", "Primer", "Science", "Tradition"];
        
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
        }
        );
    }
    return dialogContents;
}
function SetupTopicDialog() {
    $("div[data-book='topic']").click(function() {

        if ($(this).css('cursor') == 'not-allowed')
            return;

        // url = GetDictionary().urlOfUIFiles + "/topicDialog.htm";
        var dialogContents = FindOrCreateTopicDialogDiv();
        var dlg = $(dialogContents).dialog({
            autoOpen: "true",
            modal: "true",
            zIndex: 30000, //qtip is in the 15000 range
            buttons: {
                "Ok": function() {
                    var t = $("ol#topics li.ui-selected");
                    if (t.length) 
                    {
                        $("div[data-book='topic']").filter("[class~='bloom-contentNational1']").text(t[0].innerHTML);
                    }
                    $(this).dialog("close");
                }
            }
        });

        //make a double click on an item close the dialog
        dlg.find("li").dblclick(function() {
            var x = dlg.dialog("option", "buttons");
            x['Ok'].apply(dlg);
        });
    });
}
