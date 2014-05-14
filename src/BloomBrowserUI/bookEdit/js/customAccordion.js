/// <reference path="../../lib/jquery.d.ts" />

$(function () {
    $("#accordion").accordion({
        heightStyle: "fill"
    });
});

$(function () {
    $("#editControlsRoot").resizable({
        minHeight: 300,
        minWidth: 200,
        resize: function () {
            $("#accordion").accordion("refresh");
        }
    });
});
//# sourceMappingURL=customAccordion.js.map
