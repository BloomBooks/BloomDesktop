var CalledByCSharp = (function () {
    function CalledByCSharp() {
    }
    CalledByCSharp.prototype.handleUndo = function () {
        // Stuff "in the toolbox" (not clear what that means) gets its own undo handling
        var contentWindow = this.getToolboxContent();
        if (contentWindow && contentWindow.model && contentWindow.model.shouldHandleUndo()) {
            contentWindow.model.undo();
        } // elsewhere, we try to ask ckEditor to undo, else just the document
        else {
            var ckEditorUndo = this.ckEditorUndoCommand();
            if (ckEditorUndo === null || !ckEditorUndo.exec()) {
                //sometimes ckEditor isn't active, so it wasn't paying attention, so it can't do the undo. So ask the document to do an undo:
                this.getPageContent().document.execCommand('undo', false, null);
            }
        }
    };
    CalledByCSharp.prototype.ckEditorUndoCommand = function () {
        try {
            return this.getPageContent().CKEDITOR.instances.editor1.commands.undo;
        }
        catch (e) {
            return null;
        }
    };
    CalledByCSharp.prototype.canUndo = function () {
        var contentWindow = this.getToolboxContent();
        if (contentWindow && contentWindow.model && contentWindow.model.shouldHandleUndo()) {
            return contentWindow.model.canUndo();
        }
        /* I couldn't find a way to ask ckeditor if it is ready to do an undo.
          The "canUndo()" is misleading; what it appears to mean is, can this command (undo) be undone?*/
        /*  var ckEditorUndo = this.ckEditorUndoCommand();
            if (ckEditorUndo === null) return 'fail';
            return ckEditorUndo.canUndo() ? 'yes' : 'no';
        */
        return "fail"; //go ask the browser
    };
    CalledByCSharp.prototype.pageSelectionChanging = function () {
        var contentWindow = this.getPageContent();
        contentWindow['pageSelectionChanging']();
    };
    CalledByCSharp.prototype.disconnectForGarbageCollection = function () {
        var contentWindow = this.getPageContent();
        contentWindow['disconnectForGarbageCollection']();
    };
    CalledByCSharp.prototype.loadReaderToolSettings = function (settings, bookFontName) {
        var contentWindow = this.getToolboxContent();
        if (!contentWindow)
            return;
        if (typeof contentWindow['initializeSynphony'] === 'function')
            contentWindow['initializeSynphony'](settings, bookFontName);
    };
    CalledByCSharp.prototype.setSampleTextsList = function (fileList) {
        this.invokeToolboxWithOneParameter('setTextsList', fileList);
    };
    CalledByCSharp.prototype.setSampleFileContents = function (fileContents) {
        this.invokeToolboxWithOneParameter('setSampleFileContents', fileContents);
    };
    CalledByCSharp.prototype.setCopyrightAndLicense = function (contents) {
        var contentWindow = this.getPageContent();
        if (!contentWindow)
            return;
        if (typeof contentWindow['SetCopyrightAndLicense'] === 'function')
            contentWindow['SetCopyrightAndLicense'](contents);
    };
    CalledByCSharp.prototype.cleanupAudio = function () {
        var contentWindow = this.getPageContent();
        if (!contentWindow)
            return;
        if (typeof contentWindow['cleanupAudio'] === 'function') {
            contentWindow['cleanupAudio']();
        }
    };
    CalledByCSharp.prototype.setPeakLevel = function (level) {
        var toolboxWindow = this.getToolboxContent();
        if (!toolboxWindow)
            return;
        if (typeof toolboxWindow['setPeakLevel'] === 'function') {
            toolboxWindow['setPeakLevel'](level);
        }
    };
    CalledByCSharp.prototype.removeSynphonyMarkup = function () {
        var page = this.getPageContent();
        if (!page)
            return;
        if ((typeof page['jQuery'] !== 'undefined') && (page['jQuery'].fn.removeSynphonyMarkup))
            page['jQuery']('.bloom-content1').removeSynphonyMarkup();
    };
    CalledByCSharp.prototype.invokeToolboxWithOneParameter = function (functionName, value) {
        var contentWindow = this.getToolboxContent();
        if (!contentWindow)
            return;
        if (typeof contentWindow[functionName] === 'function')
            contentWindow[functionName](value);
    };
    CalledByCSharp.prototype.getPageContent = function () {
        var page = document.getElementById('page');
        return (page) ? page.contentWindow : null;
    };
    CalledByCSharp.prototype.getToolboxContent = function () {
        var toolbox = document.getElementById('toolbox');
        return (toolbox) ? toolbox.contentWindow : null;
    };
    return CalledByCSharp;
})();
//# sourceMappingURL=calledByCSharp.js.map