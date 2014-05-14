/// <reference path="../../lib/jquery.d.ts" />

interface accordionInterface extends JQuery {
    accordion(options: any): JQuery;
    resizable(options: any): JQuery;
}

$(function () {
    (<accordionInterface>$("#accordion")).accordion({
        heightStyle: "fill"
    });
});

$(function () {
    (<accordionInterface>$("#editControlsRoot")).resizable({
        minHeight: 300,
        minWidth: 200,
        resize: function () {
            (<accordionInterface>$("#accordion")).accordion("refresh");
        }
    });
});
 