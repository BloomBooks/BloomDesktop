/// <reference path="../../typings/jquery/jquery.d.ts" />
import * as $ from 'jquery';
import {getTheOneToolbox, restoreToolboxSettings, showOrHidePanel_click, removeToolboxMarkup} from './toolbox';
import {getTheOneReaderToolsModel} from './readers/readerToolsModel'
import TalkingBookModel from './talkingBook/talkingBook';
import DecodableReaderToolboxPanel from './readers/decodableReader/decodableReaderToolboxPanel';
import LeveledReaderToolboxPanel from './readers/leveledReader/leveledReaderToolboxPanel';
import {handleBookSettingCheckboxClick, handleResetZoom} from './bookSettings/bookSettings';

// each of these exports shows up under this window's FrameExports object (see bloomFrames.ts)
// reviewslog: is this actually needed? Could these be be directly imported where they are used?
export {showOrHidePanel_click};
export {removeToolboxMarkup};
export {showSetupDialog, initializeReaderSetupDialog, closeSetupDialog} from './readers/readerSetup/readerSetupDialog';
export {addWordListChangedListener, beginSaveChangedSettings, makeLetterWordList} from './readers/readerTools';
export {loadLongpressInstructions} from '../js/bloomEditing';
export {TalkingBookModel}; // one function is called by CSharp; also, exporting something from it gets it included in the bundle.
export {LeveledReaderToolboxPanel}; // just to make sure it gets included in the bundle (and adds an instance of itself to the collection in toolbox.ts)
export {DecodableReaderToolboxPanel}; // just to make sure it gets included in the bundle (and adds an instance of itself to the collection in toolbox.ts)
export {handleBookSettingCheckboxClick, handleResetZoom}; // called by click handler in jade; also, exporting something from it gets it included in the bundle.
export {getTheOneToolbox};

export function canUndo() :boolean {
    return getTheOneReaderToolsModel().shouldHandleUndo() && getTheOneReaderToolsModel().canUndo();
}

export function undo() {
    getTheOneReaderToolsModel().undo();
}


$(document).ready(function() {
    restoreToolboxSettings();
});