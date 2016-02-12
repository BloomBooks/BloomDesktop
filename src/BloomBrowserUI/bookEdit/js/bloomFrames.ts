/* The Bloom "Edit"" pane currently works by having an outer window/html with a number of iframes.
    For better or worse, these iframes currently communicate with each other.
    These functions allow any of the iframes or the root to find any of the others. Each of these
    has an "entry point" javacript which is a file bundled by webpack and <script>-included by the
    the html of that frame. 
    In order to make the contents of that bundle and the context of that frame accessible from the 
    outside, Webpack is set so that the first line of each of these "entry point" files 
    is something like 
    var FrameExports = {.....}
    
    So this module just hides all that and allows code in any frame to access the exports on any other frame.
    Not to make it simpler (because it's already simple... see how few lines are here...) but in order
    to hide the details so that we can easily change it later. 
*/

/// <reference path="../../typings/jquery/jquery.d.ts" />
// import * as $ from 'jquery';
// import { ReaderToolsWindow} from "../toolbox/decodableReader/readerToolsModel";

interface WindowWithExports extends Window {
    FrameExports: any;
}
export function getToolboxFrameExports(){
    return getFrameExports('toolbox');
}
export function getPageFrameExports(){
    return getFrameExports('page');
}
export function getEditViewFrameExports(){
    return getFrameExports('toolbox');
}

function getRootWindow(): Window{
    //if parent is null, we're the root
    return window.parent || window;
}
function getFrame(id: string): WindowWithExports{
    return (<HTMLIFrameElement>getRootWindow().document.getElementById(id)).contentWindow as WindowWithExports;
}
function getFrameExports(id: string): any{
    return getFrame(id).FrameExports;
}
