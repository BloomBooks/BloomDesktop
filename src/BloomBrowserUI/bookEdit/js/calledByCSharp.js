/// <reference path="StyleEditor.ts"/>
var CalledByCSharp = (function () {
    function CalledByCSharp() {
    }
    CalledByCSharp.prototype.handleUndo = function () {
        var contentWindow = this.getAccordionContent();
        if (!contentWindow || !contentWindow.model || !contentWindow.model.shouldHandleUndo()) {
            return 'fail';
        }
        contentWindow.model.undo();
        return 'success';
    };

    CalledByCSharp.prototype.canUndo = function () {
        var contentWindow = this.getAccordionContent();
        if (!contentWindow || !contentWindow.model || !contentWindow.model.shouldHandleUndo())
            return 'fail';
        return contentWindow.model.canUndo();
    };

    CalledByCSharp.prototype.loadReaderToolSettings = function (settings, bookFontName) {
        var contentWindow = this.getAccordionContent();
        if (!contentWindow)
            return;

        if (typeof contentWindow['initializeSynphony'] === 'function')
            contentWindow['initializeSynphony'](settings, bookFontName);
    };

    CalledByCSharp.prototype.setSampleTextsList = function (fileList) {
        this.invokeAccordionWithOneParameter('setTextsList', fileList);
    };

    CalledByCSharp.prototype.setSampleFileContents = function (fileContents) {
        this.invokeAccordionWithOneParameter('setSampleFileContents', fileContents);
    };

    CalledByCSharp.prototype.setCopyrightAndLicense = function (contents) {
        var contentWindow = this.getPageContent();
        if (!contentWindow)
            return;

        if (typeof contentWindow['SetCopyrightAndLicense'] === 'function')
            contentWindow['SetCopyrightAndLicense'](contents);
    };

    CalledByCSharp.prototype.removeSynphonyMarkup = function () {
        var page = this.getPageContent();
        if (!page)
            return;

        if ((typeof page['jQuery'] !== 'undefined') && (page['jQuery'].fn.removeSynphonyMarkup))
            page['jQuery']('.bloom-content1').removeSynphonyMarkup();
    };

    CalledByCSharp.prototype.invokeAccordionWithOneParameter = function (functionName, value) {
        var contentWindow = this.getAccordionContent();
        if (!contentWindow)
            return;

        if (typeof contentWindow[functionName] === 'function')
            contentWindow[functionName](value);
    };

    CalledByCSharp.prototype.getPageContent = function () {
        var page = document.getElementById('page');
        return (page) ? page.contentWindow : null;
    };

    CalledByCSharp.prototype.getAccordionContent = function () {
        var accordion = document.getElementById('accordion');
        return (accordion) ? accordion.contentWindow : null;
    };

    // Temporary fix for BL-516, not remembering style on linux
    CalledByCSharp.prototype.getUserModifiedStyles = function () {
        var page = this.getPageContent();
        if (typeof page['GetEditor'] !== 'function')
            return '';

        var styleEditor = page['GetEditor']();
        var sheet = styleEditor.GetOrCreateUserModifiedStyleSheet();
        var rules = [];

        for (var i = 0; i < sheet.cssRules.length; i++) {
            rules.push(sheet.cssRules[i].cssText);
        }
        return JSON.stringify(rules);
    };
    return CalledByCSharp;
})();
//# sourceMappingURL=calledByCSharp.js.map
