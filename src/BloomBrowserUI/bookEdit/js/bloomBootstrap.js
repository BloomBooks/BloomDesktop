//these are in BloomBrowserUI/node_modules. If you're missing one, do "npm install" from the BloomBrowserUI directory
var npmModules = [
    'jquery/dist/jquery.js',
    'toastr/toastr.js',
    'jquery.hotkeys/jquery.hotkeys.js'
];

var scripts = [
    'lib/jquery-ui/jquery-ui.min.js', // nb: we just rename whatever version of jquery-ui we have to this.
    'lib/jquery.easytabs.js', // we have modified this. Todo: make a github fork of our version
    'lib/jquery.hashchange.min.js',       // needed by easytabs
    'lib/jquery.qtip.js', // currently we have added a fix that may not be needed now. Upgrade?
    'lib/jquery.qtipSecondary.js',//was a duplicate to allow two qtips on same object. Come up with a more elegant way?
    'bookEdit/js/getIframeChannel.js',
    'lib/localizationManager/localizationManager.js',// comes from a bloom-specific typescript fn. Todo: make a github repo of it (with a more specific name)
    'lib/jquery.i18n.custom.js',
    'lib/jquery.sizes.js', //review: is this used? Where?
    'lib/jquery.watermark.js', //replace: no npm or repo
    'lib/jquery.myimgscale.js', // no npm just old bitbucket repo, has been customized. Todo: make github fork of our version.
    'lib/jquery.resize.js', // we have modified this. Todo: make a github fork of our version
    'lib/errorHandler.js', // comes from a bloom-specific typescript fn
    'bookEdit/js/editableDivUtils.js',
    'bookEdit/js/bloomQtipUtils.js',
    'bookEdit/sourceBubbles/bloomSourceBubbles.js',
    'bookEdit/js/bloomNotices.js',
    'bookEdit/js/bloomHintBubbles.js',
    'bookEdit/StyleEditor/StyleEditor.js',
    'bookEdit/OverflowChecker/OverflowChecker.js',
    'bookEdit/TopicChooser/TopicChooser.js',
    'lib/tabpane.js',
    'lib/ckeditor/ckeditor.js',
    'bookEdit/js/toolbar/jquery.toolbar.js',
    'bookEdit/bloomField/bloomField.js',
    'bookEdit/js/bloomImages.js',
    'bookEdit/js/bloomEditing.js',
    'lib/split-pane/split-pane.js',
    'bookEdit/js/origami.js',
    'lib/long-press/jquery.mousewheel.js',
    'lib/jquery.alphanum.js',
    'lib/long-press/jquery.longpress.js',
    // synphony stuff is currently needed just to support finding sentences in audioRecording.
    'bookEdit/toolbox/decodableReader/libsynphony/xregexp-all-min.js',
    'bookEdit/toolbox/decodableReader/libsynphony/bloom_xregexp_categories.js',
    'bookEdit/toolbox/decodableReader/libsynphony/synphony_lib.js',
    'bookEdit/toolbox/decodableReader/libsynphony/bloom_lib.js',
    'bookEdit/toolbox/talkingBook/audioRecording.js'
];

var styleSheets = [
    'themes/bloom-jqueryui-theme/jquery-ui-1.8.16.custom.css',
    'themes/bloom-jqueryui-theme/jquery-ui-dialog.custom.css',
    'lib/jquery.qtip.css',
    'bookEdit/css/qtipOverrides.css',
    'bookEdit/js/toolbar/jquery.toolbars.css',
    'bookEdit/css/origami.css',
    'bookEdit/css/tab.winclassic.css',
    'bookEdit/StyleEditor/StyleEditor.css',
    'bookEdit/css/bloomDialog.css',
    'lib/long-press/longpress.css',
    'bookEdit/toolbox/talkingBook/audioRecording.css'
];

for (var i = 0; i < npmModules.length; i++) {

    document.write('<script type="text/javascript" src="/bloom/node_modules/' + npmModules[i] + '"></script>');
}
for (var i = 0; i < scripts.length; i++) {
    document.write('<script type="text/javascript" src="/bloom/' + scripts[i] + '"></script>');
}

for (var j = 0; j < styleSheets.length; j++) {
    document.write('<link rel="stylesheet" type="text/css" href="/bloom/' + styleSheets[j] + '">');
}
