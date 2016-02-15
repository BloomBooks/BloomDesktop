/// <reference path="../../typings/jquery/jquery.d.ts" />
import * as $ from 'jquery';
import {restoreToolboxSettings, showOrHidePanel_click} from './toolbox';
import {ReaderToolsModel} from './decodableReader/readerToolsModel'
import TalkingBookModel from './talkingBook/talkingBook';
import {handleBookSettingCheckboxClick} from './bookSettings/bookSettings';

// each of these exports shows up under this window's FrameExports object (see BloomFrames.ts)
// reviewslog: is this actually needed? Could these be be directly imported where they are used?
export {showOrHidePanel_click};
export {showSetupDialog, initializeReaderSetupDialog} from './decodableReader/decodableReader'
export {addWordListChangedListener} from './decodableReader/readerTools';
export {loadLongpressInstructions} from '../js/bloomEditing';
export {default as BloomHelp} from '../../BloomHelp';
export {TalkingBookModel}; // one function is called by CSharp; also, exporting something from it gets it included in the bundle.
export {handleBookSettingCheckboxClick}; // called by click handler in jade; also, exporting something from it gets it included in the bundle.

export function canUndo() :boolean {
    return (typeof ReaderToolsModel.model != 'undefined') && ReaderToolsModel.model.shouldHandleUndo() && ReaderToolsModel.model.canUndo();
}

export function undo() {
    ReaderToolsModel.model.undo();
}

//this is currently inserted by c#. Enhance: get settings via ajax
declare function GetToolboxSettings():any;

$(document).ready(function() { 
    restoreToolboxSettings(GetToolboxSettings()); 
});