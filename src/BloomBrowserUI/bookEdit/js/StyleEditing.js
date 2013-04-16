/// <reference path="../../lib/jquery.d.ts" />
//module Bloom
//{
var StyleEditor = (function () {
    function StyleEditor(doc) {
        this.styleElement = ($(doc).find(".styleEditorStuff").first());
        //the <any> here is to turn off the typscript process erro
        if(!this.styleElement) {
            var s = $('<style id="documentStyles" class="styleEditorStuff" type="text/css"></style>');
            $(doc).find("head").append(s);
            this.styleElement = $(doc).find('.styleEditorStuff')[0];
        }
    }
    StyleEditor.prototype.GetStyleClassFromElement = function (target) {
        var classes = $(target).attr("class").split(' ');
        for(var i = 0; i < classes.length; i++) {
            if(classes[i].indexOf('Style') > 0) {
                return classes[i];
            }
        }
        return null;
    };
    StyleEditor.prototype.MakeBigger = function (target) {
        var styleName = this.GetStyleClassFromElement(target);
        //$(this.styleElement).html(styleName+ ': {color:red;}');
        $(this.styleElement).css({
            zIndex: '12'
        });
    };
    return StyleEditor;
})();
//}
//@ sourceMappingURL=StyleEditor.js.map
//@ sourceMappingURL=StyleEditing.js.map
