var scripts = document.getElementsByTagName('script');
var path = scripts[scripts.length - 1].src.split('?')[0];      // remove any ?query
var dir = path.split('/').slice(0, -1).join('/') + '/';  // remove last filename part of path

document.write('<script type="text/javascript" src="' +dir+ 'jquery-1.4.4.min.js"></script>');
document.write('<script type="text/javascript" src="' + dir + 'SignalOverflow.js"></script>');
document.write('<script type="text/javascript" src="' + dir + 'InitScripts.js"></script>');