/// <reference path="../typings/jquery/jquery.d.ts" />
/// <reference path="../typings/jqueryui/jqueryui.d.ts" />
/// <reference path="../typings/jquery.i18n.custom.d.ts" />
/// <reference path="../lib/jquery.i18n.custom.ts" />
/// <reference path="js/getIframeChannel.ts"/>
/// <reference path="js/interIframeChannel.ts"/>


import * as $ from 'jquery';
import * as jQuery from 'jquery';
import {bootstrap} from './js/bloomEditing';
import '../lib/jquery.i18n.custom.js'; //localize()
// this ended up embedding ckeditor in our big bundle, which then caused ckeditor to look for its support files in the wrong places (c[a] undefined): import '../lib/ckeditor/ckeditor.js';

var styleSheets = [
    '../themes/bloom-jqueryui-theme/jquery-ui-1.8.16.custom.css',
    '../themes/bloom-jqueryui-theme/jquery-ui-dialog.custom.css',
    '../lib/jquery.qtip.css',
    '../css/qtipOverrides.css',
    'js/toolbar/jquery.toolbars.css',
    '../css/origami.css',
    '../css/tab.winclassic.css',
    'StyleEditor/StyleEditor.css',
    '../css/bloomDialog.css',
    '../lib/long-press/longpress.css',
    '../toolbox/talkingBook/audioRecording.css'
];


for (var j = 0; j < styleSheets.length; j++) {
    document.write('<link rel="stylesheet" type="text/css" href="/bloom/' + styleSheets[j] + '">');
}

// var scripts = [
//     'bookEdit/js/getIframeChannel.js',
//     'lib/localizationManager/localizationManager.js',
//     'lib/jquery.i18n.custom.js',
   
// ];

// for (var i = 0; i < scripts.length; i++) {
//     document.write('<script type="text/javascript" src="/bloom/' + scripts[i] + '"></script>');
// }

$(document).ready(function() {
     $('body').find('*[data-i18n]').localize();
     bootstrap(); 
});