$.fn.CenterVerticallyInParent = function () {
    return this.each(function (i) {

    //TODO: this height() thing is a mystery. Whereas Firebug will say the box is, say, 53px, this will say 675, so that no centering is possible
        var ah = $(this).height();
        var ph = $(this).parent().height();
        var mh = Math.ceil((ph - ah) / 2);
        $(this).css('margin-top', mh);

        ///There is a bug in wkhtmltopdf where it determines the height of these incorrectly, causing, in a multlingual situation, the 1st text box to hog up all the room and
        //push the other guys off the page. So the hack solution of the moment is to remember the correct height here, in gecko-land, and use it over there to set the max-height.
        //See bloomPreview.SetMaxHeightForHtmlToPDFBug()
        $(this).children().each(function () {
            var h = $(this).height();
            $(this).attr('data-firefoxHeight', h);
        });
    });
};

 //Do the little bit of jscript needed even when we're just displaying the document

function SetMaxHeightForHtmlToPDFBug(element)
{
        //this comes from trying tying to find a way to work-around a bug in htmltopdf, which, when the margin-top grows, just pushes the box down & off the page.
        //we were able to make it work for one lang by doing an overflow:hidden, but for multilingual, the first div just pushes then next into oblivion.
        //I started trying to dynamically set the max-height of each div. Problem: we don't actually have a way of knowing how high they *should* be, because
        // here in wkhtmltopdf, we get the wrong value (that's what got us in this fix in the first place).
        //The hack for now is to, over in the editing javascript, remember the proper height while we're still in firefox, then use it here in wkhtmltopdf
        $(element).children().each(function(){
          if($(this).attr('data-firefoxHeight') != undefined){
                $(this).css('max-height', $(this).attr('data-firefoxHeight'));
          };
        });
}

jQuery(document).ready(function () {

    $('textarea').focus(function () { $(this).attr('readonly', 'readonly'); });

    //make images scale up to their container without distorting their proportions, while being centered within it.
    $(".bloom-imageContainer img").scaleImage({ scale: "fit" }); //uses jquery.myimgscale.js

    $(".bloom-page").mouseenter(function(){$(this).addClass("disabledVisual")});
    $(".bloom-page").mouseleave(function(){$(this).removeClass("disabledVisual")});

        //Allow labels and separators to be marked such that if the user doesn't fill in a value, the label will be invisible when published.
    //NB: why in cleanup? it's not ideal, but if it gets called after each editing session, then things will be left in the proper state.
    //If we ever get into jscript at publishing time, well then this could go there.
    $("*.bloom-doNotPublishIfParentOtherwiseEmpty").each(function() {
        if ($(this).parent().find('*:empty').length > 0) {
            $(this).addClass('bloom-hideWhenPublishing');
        }
        else {
            $(this).removeClass('bloom-hideWhenPublishing');
        }
    });


    //--------------------------------
    //keep divs vertically centered (yes, I first tried *all* the css approaches, they don't work for our situation)

    //TODO: this is't working yet, (see CenterVerticallyInParent) but in any case one todo is to trigger
    //on something different. When the user invokes "layout-style-SplitAcrossPages" mode (e.g. via the menu in the publish tab),
    //then we want to impose this on text that wouldn't
    //normally have it (e.g. it might be normally centered top or bottom).
    //Put another way, we need to eventually do this centering based on the page style, not the class on the element.
    //Like I say, it doesn't work yet anyhow...

    $(".bloom-centerVertically").CenterVerticallyInParent();
    
    $(".bloom-centerVertically").each(function () { SetMaxHeightForHtmlToPDFBug($(this)) });

});

