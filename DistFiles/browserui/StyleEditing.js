function MakeBoxTextSmaller(box) {
    alert("hello");
}
var ThomasArdal;
(function (ThomasArdal) {
    var StringGenerator = (function () {
        function StringGenerator() { }
        StringGenerator.prototype.generate = function (input) {
            return input + ": Hello World";
        };
        return StringGenerator;
    })();
    ThomasArdal.StringGenerator = StringGenerator;    
})(ThomasArdal || (ThomasArdal = {}));
var StyleEditor = (function () {
    function StyleEditor(document) {
        this.document = document;
        this.styleElement = $(document).find("#documentStyles");
        if(this.styleElement == null) {
            $(document).find("head").appendChild("style");
        }
    }
    StyleEditor.prototype.SayYes = function () {
        return "yes";
    };
    return StyleEditor;
})();
exports.StyleEditor = StyleEditor;
//@ sourceMappingURL=StyleEditing.js.map
