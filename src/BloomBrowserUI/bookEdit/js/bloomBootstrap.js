var scripts = [
    'lib/jquery-1.10.1.js',               // nb: we just rename whatever version of jquery we have to this.
    'lib/jquery-ui-1.10.3.custom.min.js', // nb: we just rename whatever version of jquery-ui we have to this.
    'lib/jquery.easytabs.js',
    'lib/jquery.hashchange.min.js',       // needed by easytabs
    'lib/jquery.qtip.js',
    'lib/jquery.qtipSecondary.js',
    'bookEdit/js/getIframeChannel.js',
    'lib/localizationManager.js',
    'lib/jquery.i18n.custom.js',
    'lib/jquery.sizes.js',
    'lib/jquery.watermark.js',
    'lib/jquery.myimgscale.js',
    'lib/jquery.resize.js',
    'bookEdit/js/jquery.hotkeys.js',
    'bookEdit/js/BloomAccordion.js',
    'bookEdit/StyleEditor/StyleEditor.js',
    'lib/tabpane.js',
    'bookEdit/js/toolbar/jquery.toolbar.js',
    'bookEdit/js/libsynphony/underscore_min_152.js', // start of SynPhony support files
    'bookEdit/js/libsynphony/xregexp-all-min.js',
    'bookEdit/js/libsynphony/bloom_xregexp_categories.js',
    'bookEdit/js/libsynphony/jquery.text-markup.js',
    'bookEdit/js/jquery.div-columns.js',
    'bookEdit/js/libsynphony/synphony_lib.js',
    'bookEdit/js/libsynphony/bloom_lib.js',
    'bookEdit/js/readerSettings.js',
    'bookEdit/js/synphonyApi.js',
    'bookEdit/js/readerToolsModel.js',
    'bookEdit/js/readerTools.js', // end of SynPhony support files
    'bookEdit/js/bloomEditing.js',
    'lib/split-pane/split-pane.js',
    'bookEdit/js/origami.js',
    'lib/long-press/jquery.mousewheel.js',
    'lib/jquery.alphanum.js',
    'lib/long-press/jquery.longpress.js'
];

var styleSheets = [
    'themes/bloom-jqueryui-theme/jquery-ui-1.8.16.custom.css',
    'themes/bloom-jqueryui-theme/jquery-ui-dialog.custom.css',
    'lib/jquery.qtip.css',
    'bookEdit/js/toolbar/jquery.toolbars.css',
    'bookEdit/css/origami.css',
    'bookEdit/css/tab.winclassic.css',
    'bookEdit/StyleEditor/StyleEditor.css',
    'bookEdit/css/bloomDialog.css',
    'lib/long-press/longpress.css'
];

for (var i = 0; i < scripts.length; i++) {
    document.write('<script type="text/javascript" src="/bloom/' + scripts[i] + '"></script>');
}

for (var j = 0; j < styleSheets.length; j++) {
    document.write('<link rel="stylesheet" type="text/css" href="/bloom/' + styleSheets[j] + '">');
}
