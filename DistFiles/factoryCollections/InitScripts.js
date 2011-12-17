//Run any needed functions here:
jQuery(document).ready(function () {

    //make textarea edits go back into the dom (they were designed to be POST'ed via forms)
    jQuery("textarea").blur(function () { this.innerHTML = this.value; });

    //when a textarea gets focus, send Bloom a dictionary of all the translations found within
    //the same parent element
    jQuery("textarea").focus( function() {
        event = document.createEvent('MessageEvent');
        var origin = window.location.protocol + '//' + window.location.host;
        var obj = {};
        $(this).parent().find("textarea").each( function(){
                obj[$(this).attr("lang")] = $(this).text();
            })
            var json = obj;//.get();
            json = JSON.stringify(json);
            event.initMessageEvent ('textGroupFocussed', true, true, json, origin, 1234, window, null);
            document.dispatchEvent (event);
        });


    //when a textarea is overfull, add the overflow class so that it gets a red background or something
    //NB: we would like to run this even when there is a mouse paste, but currently don't know how
    //to get that event. You'd think change would do it, but it doesn't. http://stackoverflow.com/questions/3035633/jquery-change-not-working-incase-of-dynamic-value-change
    jQuery("textarea").change( function() {
        var overflowing = this.scrollHeight > this.clientHeight;
        if($(this).hasClass('overflow') && !overflowing){
            $(this).removeClass('overflow');
        }
        else if(overflowing)     {
            $(this).addClass('overflow');
        }
    });

    //put hint bubbles next to elements which call for them
    $("*[data-hint]").each(function() {
        var whatToSay = $(this).data("hint");
        $(this).qtip({
           content: whatToSay,
            position: {
                at: 'right center',
                my: 'left center'
             },
             show: {
                event: false, // Don't specify a show event...
                ready: true // ... but show the tooltip when ready
             },
             hide: false,
            style: {
               classes: 'ui-tooltip-shadow ui-tooltip-plain'            },
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
                render: function(event, api) {
                    $('*[oldtitle]').each(function() {
                        $(this)[0].removeAttribute('aria-describedby');
                    });
                }
               }
        });
    });


    //make images look click-able when you cover over them
    jQuery("img").hover(function() {
				$(this).addClass('hoverUp')
			},function() {
							$(this).removeClass('hoverUp')
						});


    //focus on the first editable field
    $(':input:enabled:visible:first').focus();

});