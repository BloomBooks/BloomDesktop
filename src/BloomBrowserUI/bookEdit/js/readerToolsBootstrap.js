var readerToolsScripts = [
    'bookEdit/js/libsynphony/underscore_min_152.js',
    'bookEdit/js/libsynphony/xregexp-all-min.js',
    'bookEdit/js/libsynphony/bloom_xregexp_categories.js',
    'bookEdit/js/libsynphony/jquery.text-markup.js',
    'bookEdit/js/jquery.div-columns.js',
    'bookEdit/js/libsynphony/synphony_lib.js',
    'bookEdit/js/libsynphony/bloom_lib.js',
    'bookEdit/js/readerSettings.js',
    'bookEdit/js/synphonyApi.js',
    'bookEdit/js/readerToolsModel.js',
    'bookEdit/js/readerTools.js'
];

for (var i = 0; i < readerToolsScripts.length; i++) {
    document.write('<script type="text/javascript" src="/bloom/' + readerToolsScripts[i] + '"></script>');
}
