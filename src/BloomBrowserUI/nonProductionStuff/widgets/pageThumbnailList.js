
jQuery(document).ready(function () {

    buildThumbnailList = function(url) {
        var iframe = document.createElement("iframe");
        iframe.style.visibility = "hidden";
        iframe.src = url;
        $(iframe).bind("load", function()
        {
            onFrameLoaded(iframe);
        });
        window.document.body.appendChild(iframe);
    };

    onFrameLoaded = function( frame ) {
        $(frame.contentWindow.document.documentElement).find("div").each(function(){
            $(this).css('display','none'); //hide all the pages
        });
        var ZOOM = 0.25;

        $(frame.contentWindow.document.documentElement).find("div").each(function(){

            $(this).css('display','block');//show this one page

             var width = this.scrollWidth;
             var height = this.scrollHeight;

             var canvas = document.createElement("canvas");
                 canvas.width = width*ZOOM;
                 canvas.height = height*ZOOM;

            var ctx = canvas.getContext("2d");
            ctx.save();
            ctx.scale(ZOOM,ZOOM);
            netscape.security.PrivilegeManager.enablePrivilege("UniversalBrowserRead");
            ctx.drawWindow(frame.contentWindow,
                           0, 0,
                           width, height,
                           "rgb(255,255,255)");
            ctx.restore();

            var item =   $('<li>');
            item.width = canvas.width;
            item.height = canvas.height;
            item.append(canvas);
            item.click(function() {
                $(this).addClass("ui-selected").siblings().removeClass("ui-selected");
            });
            $('ul').append(item);
            $('ul').css('width',(2*item.width)+60);

            $(this).css('display','none');//done with it, so hide this one page
        });
    };

    $('ul').empty();
    var url = "file:///C:/dev/Bloom/DistFiles/factoryCollections/listPageTest/Vaccinations.htm";
    buildThumbnailList(url);

    //       /* this makes it selectable and sortable, at the cost of having to have a handle. From http://forum.jquery.com/topic/is-it-possible-to-make-selectable-a-sortable */
    //       $( "ul" )
    //          .sortable({ handle: ".handle" })
    //          .selectable()
    //            .find( "li" )
    //              .addClass( "ui-corner-all" )
    //              .prepend( "<div class='handle'><span class='ui-icon ui-icon-triangle-1-se'></span></div>" );
    //
    //        /* this makes it so that only one item is selectable at a time. see:  http://forum.jquery.com/topic/selectable-single-select*/
    //        $("ul").selectable({
    //              selected: function(event, ui) {
    //                    $(ui.selected).siblings().removeClass("ui-selected");
    //              }
    //        });
    //
            //NB: as of Dec 2011, jquery doesn't expect you to have selectable and sortable (the handle trick above gets around this), or single-selectable (the 2nd trick above gets around that)

            $( "ul" ).sortable();


});