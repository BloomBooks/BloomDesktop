var scripts = document.getElementsByTagName('script');
var path = scripts[scripts.length - 1].src.split('?')[0];      // remove any ?query
var thisDir = path.split('/').slice(0, -1).join('/') + '/';  // remove last filename part of path
var lib = thisDir + "../../lib/";
document.write('<script type="text/javascript" src="' + lib + 'jquery-1.9.1.js"></script>');//nb: we just rename whatever version of jquery we have to this.
document.write('<script type="text/javascript" src="' + lib + 'jquery.myimgscale.js"></script>');
document.write('<script type="text/javascript" src="' + thisDir + 'bloomPreview.js"></script>');
