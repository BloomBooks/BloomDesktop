/// <reference path="../../typings/jquery/jquery.d.ts" />
import * as $ from 'jquery';
import {restoreToolboxSettings} from './toolbox';

import {ReaderToolsModel} from './decodableReader/readerToolsModel'

export function canUndo() :boolean {
    return ReaderToolsModel.model !== null && ReaderToolsModel.model.shouldHandleUndo() && ReaderToolsModel.model.canUndo();
}

export function undo() {
    ReaderToolsModel.model.undo();
}

//this is currently inserted by c#. TODO: get settings via ajax
declare function GetToolboxSettings():any;

$(document).ready(function() { 
    restoreToolboxSettings(GetToolboxSettings()); 
});