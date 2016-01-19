var path = require('path');
var webpack = require('webpack');
var node_modules = path.resolve(__dirname, 'node_modules');
var pathToReact = path.resolve(node_modules, 'react/dist/react.min.js');
var pathToReactDom = path.resolve(node_modules, 'react-dom/dist/react-dom.min.js');

module.exports = {
    context: __dirname,
    
    //Bloom is not (yet) one webapp; it's actually a several loosely related ones. 
    //So we have multiple "entry points" that we need to emit. Fortunately the
    //CommonsChunkPlugin extracts the code that is common to more than one into "commonCode.js"
    entry: { //pageThumbnailsApp:
             toolboxIFrame: './bookEdit/toolbox/index.js',
             //editViewApp:  './bookEdit/index.js',
             //settingsIFrame:
             //pageIFrame:
             //ReaderSetupDialog:
             },

    output: {
        path: path.join(__dirname, './output/'), //NB: this is ignored if run from gulp
        filename: '[name].js'
    },
    resolve: {
        root: ['.'],
        alias: {
              'react-dom': pathToReactDom,
              'react': pathToReact // the point of this is to use the minified version. https://christianalfoni.github.io/react-webpack-cookbook/Optimizing-rebundling.html
            },
        modulesDirectories: ["lib","node_modules"],
        extensions: ['', '.js', '.jsx'] //We may need to add .less here... otherwise maybe it will ignore them unless they are require()'d
    },
    plugins: [
             new webpack.optimize.CommonsChunkPlugin("common", "commonCode.js")
      ],
    module: {

        loaders: [
           {
               test: /\.(js|jsx)$/,
               exclude: /node_modules/,
               loader: 'babel?presets[]=react,presets[]=es2015'
           },
            // { test: /\.ts(x?)$/, loader: 'babel-loader!ts-loader' },
            // { test: /\.less$/, loader: "style!css!less" }
        ],
        noParse: [pathToReactDom,pathToReact],
        plugins: [

            //answer on various legacy issues: http://stackoverflow.com/questions/28969861/managing-jquery-plugin-dependency-in-webpack?lq=1
            //prepend var $ = require("jquery") every time it encounters the global $ identifier or "jQuery".
            webpack.ProvidePlugin({
                $: "jquery",
                jQuery: "jquery",
                "window.jQuery": "jquery"
            })
    ]
    }
};
