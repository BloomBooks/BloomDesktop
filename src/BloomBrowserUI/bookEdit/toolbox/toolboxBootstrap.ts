/// <reference path="../../typings/jquery/jquery.d.ts" />
import * as $ from 'jquery';
import {restoreToolboxSettings, showOrHidePanel_click} from './toolbox';
import {ReaderToolsModel} from './decodableReader/readerToolsModel'

// each of these exports shows up under this window's FrameExports object (see BloomFrames.ts)
// reviewslog: is this actually needed? Could these be be directly imported where they are used?
export {showOrHidePanel_click};
export {showSetupDialog, initializeReaderSetupDialog} from './decodableReader/decodableReader'
export {addWordListChangedListener} from './decodableReader/readerTools';
export {loadLongpressInstructions} from '../js/bloomEditing';
export {default as BloomHelp} from '../../BloomHelp';

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