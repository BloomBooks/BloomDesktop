//review: does this actually do anything? After all, the whole point is that this isn't global
import * as $ from 'jquery';
import * as jQuery from 'jquery';

import "jquery";
import "toastr";
import "jquery.hotkeys";
import '../modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js';

import '../lib/jquery.easytabs.js'; // we have modified this. Todo: make a github fork of our version
import '../lib/jquery.hashchange.min.js';       // needed by easytabs
import '../lib/jquery.qtip.js'; // currently we have added a fix that may not be needed now. Upgrade?
import '../lib/jquery.qtipSecondary.js';//was a duplicate to allow two qtips on same object. Come up with a more elegant way?
import './js/getIframeChannel.js';
import '../lib/localizationManager/localizationManager.js';// comes from a bloom-specific typescript fn. Todo: make a github repo of it (with a more specific name)
import '../lib/jquery.i18n.custom.js';
import '../lib/jquery.sizes.js'; //review: is this used? Where?
import '../lib/jquery.watermark.js'; //replace: no npm or repo
import '../lib/jquery.myimgscale.js'; // no npm just old bitbucket repo; has been customized. Todo: make github fork of our version.
import '../lib/jquery.resize.js'; // we have modified this. Todo: make a github fork of our version
import '../lib/errorHandler.js'; // comes from a bloom-specific typescript fn
import './js/editableDivUtils.js';
import './js/bloomQtipUtils.js';
import './sourceBubbles/BloomSourceBubbles.js';
import './js/bloomNotices.js';
import './js/BloomHintBubbles.js';
import './StyleEditor/StyleEditor.js';
import './OverflowChecker/OverflowChecker.js';
import './TopicChooser/TopicChooser.js';
import '../lib/tabpane.js';
import '../lib/ckeditor/ckeditor.js'; // enhance looks like it will be in npm soon: https://github.com/ckeditor/ckeditor-releases/issues/37 
import './js/toolbar/jquery.toolbar.js';
import './bloomField/BloomField.js';
import './js/bloomImages.js';
//not yet: it's not a module import './js/bloomEditing.js';
import '../lib/split-pane/split-pane.js';
import './js/origami.js';
import '../lib/long-press/jquery.mousewheel.js';
import '../lib/jquery.alphanum.js';
import '../lib/long-press/jquery.longpress.js';
    // synphony stuff is currently needed just to support finding sentences in audioRecording.
import './toolbox/decodableReader/libsynphony/bloom_xregexp_categories.js';
import './toolbox/decodableReader/libsynphony/synphony_lib.js';
import './toolbox/decodableReader/libsynphony/bloom_lib.js';
    //Review: I'm taking this out hoping it's not needed outside the toolbox: 'toolbox/talkingBook/audioRecording.js'


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

$(document).ready(function() { $('body').find('*[data-i18n]').localize(); });