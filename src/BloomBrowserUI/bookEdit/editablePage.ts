/// <reference path="../typings/bundledFromTSC.d.ts" />
/// <reference path="../typings/jquery.i18n.custom.d.ts" />
/// <reference path="../lib/jquery.i18n.custom.ts" />

import * as $ from 'jquery';
import * as jQuery from 'jquery';
import { bootstrap } from './js/bloomEditing';
import { EditableDivUtils } from './js/editableDivUtils';
import '../lib/jquery.i18n.custom.ts'; //localize()
import '../lib/jquery.myimgscale.js'; //scaleImage()

// This exports the functions that should be accessible from other IFrames or from C#.
// For example, FrameExports.getPageFrameExports().pageSelectionChanging() can be called.
import { pageSelectionChanging } from './js/bloomEditing';
export { pageSelectionChanging };
import { disconnectForGarbageCollection } from './js/bloomEditing';
export { disconnectForGarbageCollection };
import { origamiCanUndo } from './js/origami';
export { origamiCanUndo }
import { origamiUndo } from './js/origami';
export { origamiUndo }

var styleSheets = [
    'themes/bloom-jqueryui-theme/jquery-ui-1.8.16.custom.css',
    'themes/bloom-jqueryui-theme/jquery-ui-dialog.custom.css',
    'lib/jquery.qtip.css',
    'bookEdit/css/qtipOverrides.css',
    'js/toolbar/jquery.toolbars.css',
    'bookEdit/css/origami.css',
    'bookEdit/css/tab.winclassic.css',
    'StyleEditor/StyleEditor.css',
    'bookEdit/TextBoxProperties/TextBoxProperties.css',
    'bookEdit/css/bloomDialog.css',
    'lib/long-press/longpress.css',
    'bookEdit/toolbox/talkingBook/audioRecording.css'
];

// This is using an implementation secret of a particular version of ckeditor; but it seems to
// be the only way to get at whether ckeditor thinks there is something it can undo.
// And we really NEED to get at the ckeditor undo mechanism, since ckeditor intercepts paste
// in such a way that after a paste the C# browser object answers false to CanUndo.
export function ckeditorCanUndo(): boolean {
    // review: do we need to examine all instances?
    if (CKEDITOR && CKEDITOR.currentInstance
        && (<any>CKEDITOR.currentInstance).undoManager
        && (<any>CKEDITOR.currentInstance).undoManager.undoable()) {
        return true;
    }
    return false;
}

export function ckeditorUndo() {
    // review: do we need to examine all instances?
    (<any>CKEDITOR.currentInstance).undoManager.undo();
}


for (var j = 0; j < styleSheets.length; j++) {
    document.write('<link rel="stylesheet" type="text/css" href="/bloom/' + styleSheets[j] + '">');
}


// TODO: move script stuff out of book.AddJavaScriptForEditing() and into here:
//var scripts = [
//     'lib/localizationManager/localizationManager.js',
//     'lib/jquery.i18n.custom.js',

// ];

// for (var i = 0; i < scripts.length; i++) {
//     document.write('<script type="text/javascript" src="/bloom/' + scripts[i] + '"></script>');
// }
import TopicChooser from './TopicChooser/TopicChooser';


//ShowTopicChooser() is called by a script tag on a <a> element in a tooltip
window['ShowTopicChooser'] = () => {
    TopicChooser.showTopicChooser();
}

//PasteImageCredits() is called by a script tag on a <a> element in a tooltip
window['PasteImageCredits'] = () => {
    EditableDivUtils.pasteImageCredits();
}

$(document).ready(function () {

    $('body').find('*[data-i18n]').localize();
    bootstrap();
});

export function SayHello() { alert('hello from editable page frame.'); }
