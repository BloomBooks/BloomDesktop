import $ from "jquery";
$.fn.hasAttr = function (name) {
    var attr = $(this).attr(name);

    // For some browsers, `attr` is undefined; for others,
    // `attr` is false.  Check for both.
    return typeof attr !== "undefined" && attr !== false;
};

//reviewSlog where do I belong

$.fn.CenterVerticallyInParent = function () {
    return this.each(function (i) {
        var $this = $(this);
        $this.css("margin-top", 0); // reset before calculating in case of previously messed up page
        var diff = GetDifferenceBetweenHeightAndParentHeight($this);
        if (diff < 0) {
            // we're too big, do nothing to margin-top
            // but the formatButton may need adjusting, in StyleEditor
            return;
        }
        var mh = Math.ceil(diff / 2);
        $(this).css("margin-top", mh);
    });
};
