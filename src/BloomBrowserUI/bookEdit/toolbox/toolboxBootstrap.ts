/// <reference path="../../typings/jquery/jquery.d.ts" />
import * as $ from 'jquery';
import {restoreToolboxSettings} from './toolbox';

import {ReaderToolsModel} from './decodableReader/readerToolsModel'

// These functions or classes should be available for calling by non-module code (such as C# directly)
// using the FrameExports object (see more details in BloomFrames.ts)
import BloomHelp from '../../BloomHelp';
export {BloomHelp};

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