var CalledByCSharp = (function () {
    function CalledByCSharp() {
    }
    CalledByCSharp.prototype.restoreAccordionSettings = function (settings) {
        this.invokeAccordionWithOneParameter('restoreAccordionSettings', settings);
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
    return CalledByCSharp;
})();
//# sourceMappingURL=calledByCSharp.js.map
