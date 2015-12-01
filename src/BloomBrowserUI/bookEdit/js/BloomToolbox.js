/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />
var BloomToolbox = (function () {
    function BloomToolbox() {
        $("#toolbox").accordion({
            heightStyle: "fill"
        });
    }
    BloomToolbox.Resize = function () {
        $("#toolbox").accordion("refresh");
        //var myHeight = $(document).find(".toolboxRoot").innerHeight();
        //console.log("Refreshed toolbox to: "+myHeight);
    };
    return BloomToolbox;
})();
//# sourceMappingURL=BloomToolbox.js.map