/* The Bloom "Edit"" pane currently works by having an outer window/html with a number of iframes.
    For better or worse, these iframes currently communicate with each other.
    These functions allow any of the iframes or the root to find any of the others. Each of these
    has an "entry point" javacript which is a file bundled by webpack and <script>-included by the
    the html of that frame. 
    In order to make the contents of that bundle and the context of that frame accessible from the 
    outside, Webpack is set so that the first line of each of these "entry point" files 
    is something like 
    var Exports['ToolboxIFrame'] = {.....}
    
    So this module just hides all that an allows code in any frame to access the exports on any other frame.
*/

/// <reference path="../../typings/jquery/jquery.d.ts" />
// import * as $ from 'jquery';
// import { ReaderToolsWindow} from "../toolbox/decodableReader/readerToolsModel";

interface WindowWithExports extends Window {
    Exports: any;
}
export function getToolboxFrameMethods(){
    var toolbox = (<HTMLIFrameElement>document.getElementById('toolbox')).contentWindow as WindowWithExports;
    return toolbox.Exports.toolboxIFrame;
}
export function getPageFrameMethods(){
    var page = (<HTMLIFrameElement>document.getElementById('page')).contentWindow as WindowWithExports;
    return page.Exports.editablePageIFrame;
}

function getRootWindow(): Window{
    //if parent is null, we're the root
    return window.parent || window;
}
