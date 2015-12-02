var readerToolsScripts = [
    'bookEdit/toolbox/decodableReader/libsynphony/underscore_min_152.js',
    'bookEdit/toolbox/decodableReader/libsynphony/xregexp-all-min.js',
    'bookEdit/toolbox/decodableReader/libsynphony/bloom_xregexp_categories.js',
    'bookEdit/toolbox/decodableReader/libsynphony/jquery.text-markup.js',
    'bookEdit/js/jquery.div-columns.js',
    'bookEdit/toolbox/decodableReader/libsynphony/synphony_lib.js',
    'bookEdit/toolbox/decodableReader/libsynphony/bloom_lib.js',
    'bookEdit/toolbox/decodableReader/readerSettings.js',
    'bookEdit/toolbox/decodableReader/synphonyApi.js',
    'bookEdit/toolbox/decodableReader/readerToolsModel.js',
    'bookEdit/toolbox/decodableReader/readerTools.js'
];

for (var i = 0; i < readerToolsScripts.length; i++) {
    document.write('<script type="text/javascript" src="/bloom/' + readerToolsScripts[i] + '"></script>');
}
