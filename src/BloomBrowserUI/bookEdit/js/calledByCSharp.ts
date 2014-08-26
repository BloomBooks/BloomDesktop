
class CalledByCSharp {

    restoreAccordionSettings(settings: string) {
        this.invokeAccordionWithOneParameter('restoreAccordionSettings', settings);
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
}
