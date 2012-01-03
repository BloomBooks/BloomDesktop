
jQuery(document).ready(function () {

    //make textarea edits go back into the dom (they were designed to be POST'ed via forms)
    jQuery("textarea").blur(function () { this.innerHTML = this.value; });



    //when a textarea gets focus, send Bloom a dictionary of all the translations found within
    //the same parent element
    jQuery("textarea, div.editable").focus(function () {
        event = document.createEvent('MessageEvent');
        var origin = window.location.protocol + '//' + window.location.host;
        var obj = {};
        $(this).parent().find("textarea, div.editable").each(function () {
            obj[$(this).attr("lang")] = $(this).text();
        })
        var json = obj; //.get();
        json = JSON.stringify(json);
        event.initMessageEvent('textGroupFocused', true, true, json, origin, 1234, window, null);
        document.dispatchEvent(event);
    });


    //when a textarea or div is overfull, add the overflow class so that it gets a red background or something
    //NB: we would like to run this even when there is a mouse paste, but currently don't know how
    //to get that event. You'd think change() would do it, but it doesn't. http://stackoverflow.com/questions/3035633/jquery-change-not-working-incase-of-dynamic-value-change
    jQuery("textarea").keypress(function () {
        var overflowing = this.scrollHeight > this.clientHeight;
        if ($(this).hasClass('overflow') && !overflowing) {
            $(this).removeClass('overflow');
        }
        else if (overflowing) {
            $(this).addClass('overflow');
        }
    });
    jQuery("div.editable").keypress(function () {
        var overflowing = this.scrollHeight > $(this).maxSize().height;
        if ($(this).hasClass('overflow') && !overflowing) {
            $(this).removeClass('overflow');
        }
        else if (overflowing) {
            $(this).addClass('overflow');
        }
    });

    //put hint bubbles next to elements which call for them
    $("*[data-hint]").each(function () {
        if($(this).css('border-bottom-color') == 'transparent')
        {
            return; //don't put tips if they can't edit it. That's just confusing
        }
        if($(this).css('display') == 'none')
        {
            return; //don't put tips if they can't see it.
        }
        theClasses='ui-tooltip-shadow ui-tooltip-plain';
        pos =  {        at: 'right center',
                        my: 'left center'
               };

        var whatToSay = $(this).data("hint");
        $(this).qtip({
            content: whatToSay,
            position: pos,
            show: {
                event: false, // Don't specify a show event...
                ready: true // ... but show the tooltip when ready
            },
            hide: false,
            style: {
                classes: theClasses
            },
            //the following is to limit how much stuff qtip leaves in our DOM
            //since we actually save the dom, we dont' want this stuff
            //1) we're using data-hint instead of title. That makes it easy
            //to clean up (with title, qtip moves it to oldtitle, and if we
            //move it back below, well now we also get standard browser tooltips.
            //2) we prerender
            //3) after the render, we clean up this aria-describedby attr
            //4) somebody needs to call the qtipCleanupFunction to remove the div
            prerender: true,
            events: {
                render: function (event, api) {
                    $('*[oldtitle]').each(function () {
                        $(this)[0].removeAttribute('aria-describedby');
                    });
                }
            }
        });
    });

    $.fn.hasAttr = function(name) {
        var attr = $(this).attr(name);

        // For some browsers, `attr` is undefined; for others,
        // `attr` is false.  Check for both.
        return (typeof attr !== 'undefined' && attr !== false);
    };

    //Show data on fields
           $("*[data-book], *[data-library], *[lang]").each(function () {
               var data=" ";
              if($(this).hasAttr("data-book")) {
                  data = $(this).attr("data-book");
              }
              if($(this).hasAttr("data-library")) {
                  data = $(this).attr("data-library") ;
              }
              $(this).qtipSecondary({
                  content: { text:  $(this).attr("lang") + "<br>"+ data},//, title: { text:  $(this).attr("lang")}},

                  position:  {
                      my: 'top right',
                      at: 'top left'
                  },
//                  show: {
//                                  event: false, // Don't specify a show event...
//                                  ready: true // ... but show the tooltip when ready
//                              },
//                  hide:false,//{     fixed: true },// Make it fixed so it can be hovered over    },
                  style: {'default': false,
                      tip: {corner: false, border:false},
                      classes: 'fieldInfo-qtip'
                  }
              });
          });

    cleanup = function(){
        return;
        // remove the div's which qtip makes for the tips themselves
        $("div.qtip").each(function() {
              $(this).remove();
        })
        // remove the attributes qtips adds to the things being annotated
        $("*[aria-describedby]").each(function() {
            $(this).removeAttr("aria-describedby");
        })
        $("*[ariasecondary-describedby]").each(function() {
            $(this).removeAttr("ariasecondary-describedby");
        })
    }

    //make images look click-able when you cover over them
    jQuery("img").hover(function () {
        $(this).addClass('hoverUp')
    }, function () {
        $(this).removeClass('hoverUp')
    });


    // Bloom needs to make some field readonly. E.g., the original license when the user is translating a shellbook
    // Normally, we'd control this is a style in editTranslationMode.css. However, "readonly" isn't a style, just
    // an attribute, so it can't be included in css.
    // The solution here is to add the readonly attribute when we detect that their border has gone transparent.
    $('textarea').focus(function() {
        if($(this).css('border-bottom-color') == 'transparent'){
                    $(this).attr("readonly", "readonly");
                }
                else{
                    $(this).removeAttr("readonly");
                }
    });

    //Same thing for divs which are potentially editable.
    // editTranslationMode.css is responsible for making this transparent, but it can't reach the contentEditable attribute.
    $('div.readOnlyInTranslationMode').focus(function() {
        if($(this).css('border-bottom-color') == 'transparent'){
                    $(this).removeAttr("contentEditable");
                }
        else{
            $(this).attr("contentEditable", "true");
        }
    });

    // Send all the data from this div in a message, so Bloom can do something like show a custom dialog box
    // for editing the data. We only notice the click if the cursor style is 'pointer', so that CSS can turn this on/off.
    $('div.-bloom-metaData').each(function() {
       if($(this).css('cursor')=='pointer') {
           $(this).click(function(){
                       event = document.createEvent('MessageEvent');
                       var origin = window.location.protocol + '//' + window.location.host;
                       var obj = {};
                       $(this).find("*[data-book]").each(function () {
                           obj[$(this).attr("data-book")] = $(this).text();
                       })
                       var json = obj; //.get();
                       json = JSON.stringify(json);
                       event.initMessageEvent('divClicked', true, true, json, origin, 1234, window, null);
                       document.dispatchEvent(event);
           })
       }
    });




        //focus on the first editable field
        //$(':input:enabled:visible:first').focus();
    $("textarea, div.editable").first().focus();//review: this might chose a textarea which appears after the div. Could we sort on the tab order?



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
    $("IMG[data-book='licenseImage']").attr("src", data.licenseImage+"?"+ new Date().getTime());//the time thing makes the browser reload it even if it's the same name
    var shouldShowButton = ! $("DIV.licenseDescription").text();
    $("button#editCopyrightAndLicense").css("display",shouldShowButton ? "inline" : "none");
}
