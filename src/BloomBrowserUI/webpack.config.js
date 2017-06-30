var path = require("path");
var webpack = require("webpack");
var node_modules = path.resolve(__dirname, "node_modules");
var pathToReact = path.resolve(node_modules, "react/dist/react.js");
var pathToReactDom = path.resolve(node_modules, "react-dom/dist/react-dom.js");
var pathToOriginalJavascriptFilesInLib = path.resolve(__dirname, "lib");
var pathToBookEditJS = path.resolve(__dirname, "bookEdit/js");
var pathToOriginalJavascriptFilesInModified_Libraries = path.resolve(
  __dirname,
  "modified_libraries"
);
var globule = require("globule");

//note: if you change this, change it in gulpfile.js & karma.conf.js as well
var outputDir = "../../output/browser";

//because our output directory does not have the same parent as our node_modules
//https://github.com/babel/babel-loader/issues/166
var babelQueryString =
  "presets[]=" +
  require.resolve("babel-preset-es2015") +
  ",presets[]=" +
  require.resolve("babel-preset-react");
var babelString = require.resolve("babel-loader") + "?" + babelQueryString;

module.exports = {
  context: __dirname,
  devtool: "source-map",
  //Bloom is not (yet) one webapp; it's actually a several loosely related ones.
  //So we have multiple "entry points" that we need to emit. Fortunately the
  //CommonsChunkPlugin extracts the code that is common to more than one into "commonBundle.js"
  entry: {
    editTabRootBundle: "./bookEdit/editViewFrame.ts",
    //editablePageBootstrap: './bookEdit/editablePageBootstrap.ts',
    readerSetupBundle: "./bookEdit/toolbox/readers/readerSetup/readerSetup.ts",
    editablePageBundle: "./bookEdit/editablePage.ts",
    bookPreviewBundle: "./bookPreview/bookPreview.ts",
    toolboxBundle: "./bookEdit/toolbox/toolboxBootstrap.ts",
    pageChooserBundle: ["./pageChooser/page-chooser.ts"], //https://github.com/webpack/webpack/issues/300
    pageThumbnailListBundle:
      "./bookEdit/pageThumbnailList/pageThumbnailList.ts",
    publishUIBundle: globule.find(["./publish/**/*.tsx"]),
    testBundle: globule.find([
      "./bookEdit/**/*Spec.ts",
      "./bookEdit/**/*Spec.js",
      "./lib/**/*Spec.ts",
      "./lib/**/*Spec.js"
    ])
    //             testBundle: globule.find(["./**/*Spec.ts", "./**/*Spec.js", "!./node_modules/**"])//This slowed down webpack a ton, becuase the way it works is that it 1st it finds it all, then it excludes node_modules
  },

  output: {
    path: path.join(__dirname, outputDir),
    filename: "[name].js",

    libraryTarget: "var",

    //makes a single entry point module's epxorts accessible via Exports.
    //Note that if you include more than one entry point js in the frame, the second one will overwrite the Exports var
    // (see the other way of doing this, below, if that becomes necessary for some reason)
    library: "FrameExports"
  },

  resolve: {
    root: ["."],
    alias: {
      "react-dom": pathToReactDom,
      react: pathToReact // the point of this is to use the minified version. https://christianalfoni.github.io/react-webpack-cookbook/Optimizing-rebundling.html
    },
    modulesDirectories: [
      pathToOriginalJavascriptFilesInLib,
      node_modules,
      pathToBookEditJS,
      pathToOriginalJavascriptFilesInModified_Libraries
    ],
    extensions: ["", ".js", ".jsx", ".ts", ".tsx"] //We may need to add .less here... otherwise maybe it will ignore them unless they are require()'d
  },
  plugins: [
    new webpack.optimize.CommonsChunkPlugin("common", "commonBundle.js"),
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
      { test: /\.ts(x?)$/, loader: "ts-loader" },
      {
        test: /\.(js|jsx)$/,
        //jquery-ui is currently *not* excluded because we added some imports to it
        exclude: [
          /node_modules/,
          /ckeditor/,
          /jquery-ui/,
          /-min/,
          /qtip/,
          /xregexp-all-min.js/
        ],
        //               loader: 'babel?presets[]=react,presets[]=es2015',
        //loader: 'babel?presets[]='+__dirname+"/node_modules/babel-preset-es2015",
        loader: babelString
      }
      // { test: /\.ts(x?)$/, loader: 'babel-loader!ts-loader' },
      // { test: /\.less$/, loader: "style!css!less" }
    ]
    //noParse: [pathToReactDom, pathToReact]
  }
};
