/// <reference path="readerToolsModel.ts" />

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

    pageSelectionChanging() {
        var contentWindow = this.getPageContent();
        contentWindow['pageSelectionChanging']();
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

    recordAudio() {
      var contentWindow = this.getPageContent();
        if (!contentWindow) return;
        if (typeof contentWindow['recordAudio'] === 'function') {
            contentWindow['recordAudio']();
        }
    }

    cleanupAudio() {
        var contentWindow = this.getPageContent();
        if (!contentWindow) return;
        if (typeof contentWindow['cleanupAudio'] === 'function') {
            contentWindow['cleanupAudio']();
        }
    }

    setPeakLevel(level:string) {
        var contentWindow = this.getPageContent();
        if (!contentWindow) return;
        if (typeof contentWindow['setPeakLevel'] === 'function') {
            contentWindow['setPeakLevel'](level);
        }
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

  getAccordionContent(): ReaderToolsWindow {
    var accordion = <HTMLIFrameElement>document.getElementById('accordion');
    return (accordion) ? <ReaderToolsWindow>accordion.contentWindow : null;
  }
}
