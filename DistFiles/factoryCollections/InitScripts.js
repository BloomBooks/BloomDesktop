//Run any needed functions here:
jQuery(document).ready(function () {


    //Apply overflow handling to all textareas
    jQuery("textarea").each(function () { jQuery(this).SignalOverflow() });

    //make textarea edits go back into the dom (they were designed to be POST'ed via forms)
    jQuery("textarea").blur(function () { this.innerHTML = this.value; });
    //Other needed functions:
    //...
    //...
});