var scripts = document.getElementsByTagName('script');
var path = scripts[scripts.length - 1].src.split('?')[0];      // remove any ?query
var dir = path.split('/').slice(0, -1).join('/') + '/';  // remove last filename part of path

document.write('<script type="text/javascript" src="' + dir + 'jquery.js"></script>');//nb: we just rename whatever version of jquery we have to this.

document.write('<script type="text/javascript" src="' +dir+ 'jquery.qtip.js"></script>');
document.write('<script type="text/javascript" src="' +dir+ 'jquery.qtipSecondary.js"></script>');
document.write('<link rel="stylesheet" type="text/css" href="' + dir + 'jquery.qtip.css"></script>');

document.write('<script type="text/javascript" src="' +dir+ 'jquery.sizes.js"></script>');

document.write('<script type="text/javascript" src="' + dir + 'bloomEditing.js"></script>');
