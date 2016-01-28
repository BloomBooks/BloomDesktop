var path = require('path');
var webpack = require('webpack');
var node_modules = path.resolve(__dirname, 'node_modules');
var pathToReact = path.resolve(node_modules, 'react/dist/react.min.js');
var pathToReactDom = path.resolve(node_modules, 'react-dom/dist/react-dom.min.js');
var pathToOriginalJavascriptFilesInLib = path.resolve(__dirname, 'lib');
var pathToTranspiledJavascriptFilesInOutputLib = path.resolve(__dirname, 'output/lib');
var pathToBookEditJS = path.resolve(__dirname, 'bookEdit/js');
var pathToOriginalJavascriptFilesInModified_Libraries = path.resolve(__dirname, 'modified_libraries');

module.exports = {
    context: __dirname,
    
    //Bloom is not (yet) one webapp; it's actually a several loosely related ones. 
    //So we have multiple "entry points" that we need to emit. Fortunately the
    //CommonsChunkPlugin extracts the code that is common to more than one into "commonCode.js"
    entry: { //pageThumbnailsApp:
             editViewApp:  './bookEdit/editViewFrame.js',
             editablePageIFrame: './bookEdit/editablePageBootstrap.js',
             toolboxIFrame: './bookEdit/toolbox/toolboxBootstrap.js',
             //settingsIFrame:
             //ReaderSetupDialog:
             pageChooserIFrame: './pageChooser/js/page-chooser.js'
             },

    output: {
        path: path.join(__dirname, './output/'), //NB: this is ignored if run from gulp
        filename: "[name].js",
        //library: "GlobalAccess",
        library: ["Exports", "[name]"], //makes each entrypointModule accessible via, e.g. Exports.toolboxIFrame
        libraryTarget: "var"
    },
    resolve: {
        root: ['.'],
        alias: {
              'react-dom': pathToReactDom,
              'react': pathToReact // the point of this is to use the minified version. https://christianalfoni.github.io/react-webpack-cookbook/Optimizing-rebundling.html
            },
        modulesDirectories: [pathToOriginalJavascriptFilesInLib, pathToTranspiledJavascriptFilesInOutputLib,node_modules, pathToBookEditJS,pathToOriginalJavascriptFilesInModified_Libraries],
        extensions: ['', '.js', '.jsx'] //We may need to add .less here... otherwise maybe it will ignore them unless they are require()'d
    },
    plugins: [
           new webpack.optimize.CommonsChunkPlugin("common", "commonCode.js"),
                         //answer on various legacy issues: http://stackoverflow.com/questions/28969861/managing-jquery-plugin-dependency-in-webpack?lq=1
            //prepend var $ = require("jquery") every time it encounters the global $ identifier or "jQuery".
            new webpack.ProvidePlugin({
                $: "jquery",
                jQuery: "jquery",
                "window.jQuery": "jquery"
            })
      ],
    module: {

        loaders: [
           {
               test: /\.(js|jsx)$/,
               //jquery-ui is currently *not* excluded because we added some imports to it
               exclude: [/node_modules/, /ckeditor/, /*/jquery-ui/,*/ /-min/, /qtip/, /xregexp-all-min.js/],
               loader: 'babel?presets[]=react,presets[]=es2015'
           },
            // { test: /\.ts(x?)$/, loader: 'babel-loader!ts-loader' },
            // { test: /\.less$/, loader: "style!css!less" }
        ],
        noParse: [pathToReactDom,pathToReact]
    }
};
