//Run any needed functions here:
jQuery(document).ready(function () {
    //Apply overflow handling to all textareas
//    jQuery("textarea").each(function () { jQuery(this).SignalOverflow() });

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
});