//Do the little bit of jscript needed even when we're just displaying the document

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
});

