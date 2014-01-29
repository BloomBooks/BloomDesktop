var scripts = document.getElementsByTagName('script');
var path = scripts[scripts.length - 1].src.split('?')[0];      // remove any ?query
var thisDir = path.split('/').slice(0, -1).join('/') + '/';  // remove last filename part of path
var libDir = thisDir + "../../lib/";
var themeDir = thisDir + "../../themes/";
var fontsDir = thisDir + "../iconfont/";

document.write('<script type="text/javascript" src="' + libDir + 'jquery-1.10.1.js"></script>');//nb: we just rename whatever version of jquery we have to this.
document.write('<script type="text/javascript" src="' + libDir + 'jquery-ui-1.10.3.custom.min.js"></script>');//nb: we just rename whatever version of jqueryui we have to this.
document.write('<link rel="stylesheet" type="text/css" href="' + themeDir + 'bloom-jqueryui-theme/jquery-ui-1.8.16.custom.css">');

document.write('<script type="text/javascript" src="' + libDir + 'jquery.easytabs.js"></script>');
document.write('<script type="text/javascript" src="' + libDir + 'jquery.hashchange.min.js"></script>');//needed by easytabs

document.write('<script type="text/javascript" src="' + libDir + 'jquery.qtip.js"></script>');
document.write('<script type="text/javascript" src="' + libDir + 'jquery.qtipSecondary.js"></script>');
document.write('<link rel="stylesheet" type="text/css" href="' + libDir + 'jquery.qtip.css">');

document.write('<script type="text/javascript" src="' + libDir + 'jquery.sizes.js"></script>');
document.write('<script type="text/javascript" src="' + libDir + 'jquery.watermark.js"></script>');
document.write('<script type="text/javascript" src="' + libDir + 'jquery.myimgscale.js"></script>');
document.write('<script type="text/javascript" src="' + libDir + 'jquery.resize.js"></script>');
document.write('<script type="text/javascript" src="' + thisDir + 'bloomEditing.js"></script>');
document.write('<script type="text/javascript" src="' + thisDir + 'StyleEditor.js"></script>');

document.write('<script type="text/javascript" src="' + thisDir + 'toolbar/jquery.toolbar.js"></script>');
document.write('<link rel="stylesheet" type="text/css" href="' + thisDir + 'toolbar/jquery.toolbars.css">');
//document.write('<link rel="stylesheet" type="text/css" href="' + thisDir + 'toolbar/bootstrap.icons.css">');
//document.write('<link rel="stylesheet" type="text/css" href="' + fontsDir + 'style.css">');