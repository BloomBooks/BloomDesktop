var scripts = document.getElementsByTagName('script');
var path = scripts[scripts.length - 1].src.split('?')[0];      // remove any ?query
var dir = path.split('/').slice(0, -1).join('/') + '/';  // remove last filename part of path
var lib = dir + "lib/";

document.write('<script type="text/javascript" src="' + lib + 'jquery.js"></script>');//nb: we just rename whatever version of jquery we have to this.
document.write('<script type="text/javascript" src="' + lib + 'jquery-ui.js"></script>');//nb: we just rename whatever version of jqueryui we have to this.
document.write('<link rel="stylesheet" type="text/css" href="' + dir + '../css/bloom-theme/jquery-ui-1.8.16.custom.css"></script>');

document.write('<script type="text/javascript" src="' + lib + 'jquery.easytabs.js"></script>');
document.write('<script type="text/javascript" src="' + lib + 'jquery.hashchange.min.js"></script>');//needed by easytabs

document.write('<script type="text/javascript" src="' + lib + 'jquery.qtip.js"></script>');
document.write('<script type="text/javascript" src="' + lib + 'jquery.qtipSecondary.js"></script>');
document.write('<link rel="stylesheet" type="text/css" href="' + lib + 'jquery.qtip.css"></script>');

document.write('<script type="text/javascript" src="' + lib + 'jquery.sizes.js"></script>');
document.write('<script type="text/javascript" src="' + lib + 'jquery.watermark.js"></script>');
document.write('<script type="text/javascript" src="' + lib + 'jquery.myimgscale.js"></script>');
document.write('<script type="text/javascript" src="' + lib + 'jquery.resize.js"></script>');
document.write('<script type="text/javascript" src="' + dir + 'bloomEditing.js"></script>');
//<document.write('<script type="text/javascript" src="' + dir + 'StyleEditing.js"></script>');
