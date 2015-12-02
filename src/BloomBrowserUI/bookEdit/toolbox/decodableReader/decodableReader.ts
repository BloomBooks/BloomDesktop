/// <reference path="../toolbox.ts" />

class DecodableReaderModel implements ITabModel {
    restoreSettings(opts: string) {
        if (opts['decodableReaderState']) {
            var state = libsynphony.dbGet('drt_state');
            if (!state) state = new DRTState();
            var decState = opts['decodableReaderState'];
            if (decState.startsWith("stage:")) {
                var parts = decState.split(";");
                state.stage = parseInt(parts[0].substring("stage:".length));
                var sort = parts[1].substring("sort:".length);
                model.setSort(sort);
            } else {
                // old state
                state.stage = parseInt(decState);
            }
            libsynphony.dbSet('drt_state', state);
        }
    }

    setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // Enhance: at present, model is a global variable defined by readerToolsModel. Try to encapsulate it, or at least give a more specific name.
        // invoke function when a bloom-editable element loses focus.
        $(container).find('.bloom-editable').focusout(function() {
            model.doMarkup();
        });

        $(container).find('.bloom-editable').focusin(function() {
            model.noteFocus(this); // 'This' is the element that just got focus.
        });

        // and a slightly different one for keypresses
        $(container).find('.bloom-editable').keypress(function() {
            model.doKeypressMarkup();
        });

        $(container).find('.bloom-editable').keydown(function(e) {
            if ((e.keyCode == 90 || e.keyCode == 89) && e.ctrlKey) { // ctrl-z or ctrl-Y
                if (model.currentMarkupType !== MarkupType.None) {
                    e.preventDefault();
                    if (e.shiftKey || e.keyCode == 89) { // ctrl-shift-z or ctrl-y
                        model.redo();
                    } else {
                        model.undo();
                    }
                    return false;
                }
            }
        });
    }

    configureElements(container: HTMLElement) {
        this.setupReaderKeyAndFocusHandlers(container);
    }
}

tabModels.push(new DecodableReaderModel())