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

     $(".bloom-verticalAlign").each(function(){SetMaxHeightForHtmlToPDFBug($(this))});

});
