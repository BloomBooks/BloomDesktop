﻿/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />
var BloomAccordion = (function () {
    function BloomAccordion() {
        $("#accordion").accordion({
            heightStyle: "fill"
        });
    }
    BloomAccordion.Resize = function () {
        $("#accordion").accordion("refresh");
        //var myHeight = $(document).find(".accordionRoot").innerHeight();
        //console.log("Refreshed accordion to: "+myHeight);
    };
    return BloomAccordion;
})();
//# sourceMappingURL=BloomAccordion.js.map
