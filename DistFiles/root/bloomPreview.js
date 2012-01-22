//Do the little bit of jscript needed even when we're just displaying the document

jQuery(document).ready(function () {
    //make images scale up to their container without distorting their proportions, while being centered within it.
    $("img").scaleImage({ scale: "fit" }); //uses jquery.myimgscale.js
});

