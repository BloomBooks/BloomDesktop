UNUSED;

var scripts = document.getElementsByTagName("script");
var path = scripts[scripts.length - 1].src.split("?")[0]; // remove any ?query
var thisDir =
    path
        .split("/")
        .slice(0, -1)
        .join("/") + "/"; // remove last filename part of path
var custom = thisDir + "../../lib/";
//document.write('<script type="text/javascript" src="' + thisDir + '../../jquery.min.js"></script>');
//document.write('<script type="text/javascript" src="' + custom + 'jquery.myimgscale.js"></script>');
//document.write('<script type="text/javascript" src="' + custom + 'errorHandler.js"></script>');
//document.write('<script type="text/javascript" src="' + thisDir + 'bloomPreview.js"></script>');

//document.write('<script type="text/javascript" src="' + custom + 'localizationManager/localizationManager.js"></script>');
//document.write('<script type="text/javascript" src="' + custom + 'jquery.i18n.custom.js"></script>');
