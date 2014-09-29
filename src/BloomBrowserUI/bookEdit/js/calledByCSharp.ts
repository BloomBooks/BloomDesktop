
class CalledByCSharp {

    handleUndo(): string {
        var contentWindow = this.getAccordionContent();
        if (!contentWindow || !contentWindow.model || !contentWindow.model.shouldHandleUndo()) {
            return 'fail';
        }
        contentWindow.model.undo();
        return 'success';
    }

    canUndo(): string {
        var contentWindow = this.getAccordionContent();
        if (!contentWindow || !contentWindow.model || !contentWindow.model.shouldHandleUndo())
            return 'fail'; // we don't want to decide
        return contentWindow.model.canUndo();
    }

    loadReaderToolSettings(settings: string, bookFontName: string) {

        var contentWindow = this.getAccordionContent();
        if (!contentWindow) return;

        if (typeof contentWindow['initializeSynphony'] === 'function')
            contentWindow['initializeSynphony'](settings, bookFontName);
    }

    setSampleTextsList(fileList: string) {
        this.invokeAccordionWithOneParameter('setTextsList', fileList);
    }

    setSampleFileContents(fileContents: string) {
        this.invokeAccordionWithOneParameter('setSampleFileContents', fileContents);
    }

    setCopyrightAndLicense(contents) {

        var contentWindow = this.getPageContent();
        if (!contentWindow) return;

        if (typeof contentWindow['SetCopyrightAndLicense'] === 'function')
            contentWindow['SetCopyrightAndLicense'](contents);
    }

    removeSynphonyMarkup() {

        var page = this.getPageContent();
        if (!page) return;

        if ((typeof page['jQuery'] !== 'undefined') && (page['jQuery'].fn.removeSynphonyMarkup))
            page['jQuery']('.bloom-content1').removeSynphonyMarkup();
    }

    invokeAccordionWithOneParameter(functionName: string, value: string) {

        var contentWindow = this.getAccordionContent();
        if (!contentWindow) return;

        if (typeof contentWindow[functionName] === 'function')
            contentWindow[functionName](value);
    }

    getPageContent(): Window {
        var page = <HTMLIFrameElement>document.getElementById('page');
        return (page) ? page.contentWindow : null;
    }

    getAccordionContent(): Window {
        var accordion = <HTMLIFrameElement>document.getElementById('accordion');
        return (accordion) ? accordion.contentWindow : null;
    }
    
    // Temporary fix for BL-516, not remembering style on linux
    getUserModifiedStyles(): string {

        var page = this.getPageContent();
        if (typeof page['GetEditor'] !== 'function')
            return '';

        var styleEditor: StyleEditor = page['GetEditor']();
        var sheet: CSSStyleSheet = styleEditor.GetOrCreateUserModifiedStyleSheet();
        var rules = [];

        for (var i = 0; i < sheet.cssRules.length; i++) {
            rules.push(sheet.cssRules[i].cssText);
        }
        return JSON.stringify(rules);
    }
}
