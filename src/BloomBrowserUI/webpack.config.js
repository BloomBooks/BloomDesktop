var path = require("path");
var webpack = require("webpack");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const ShellPlugin = require("webpack-shell-plugin-next");
var node_modules = path.resolve(__dirname, "node_modules");
var pathToOriginalJavascriptFilesInLib = path.resolve(__dirname, "lib");
var pathToBookEditJS = path.resolve(__dirname, "bookEdit/js");
var pathToOriginalJavascriptFilesInModified_Libraries = path.resolve(
    __dirname,
    "modified_libraries"
);
var globule = require("globule");

//note: if you change this, change it in gulpfile.js & karma.conf.js as well
const outputDir = "../../output/browser";
const stylesDir = outputDir + "/styles";

// Because our output directory does not have the same parent as our node_modules, we
// need to resolve the babel related presets (and plugins).  This mapping function was
// suggested at https://github.com/babel/babel-loader/issues/166.
function localResolve(preset) {
    return Array.isArray(preset)
        ? [require.resolve(preset[0]), preset[1]]
        : require.resolve(preset);
}

module.exports = {
    // mode must be set to either "production" or "development" in webpack 4.
    // We'll have to figure out something if we ever want to use "production" for release builds.
    mode: "development",
    context: __dirname,
    devtool: "source-map",
    //Bloom is not (yet) one webapp; it's actually a several loosely related ones.
    //So we have multiple "entry points" that we need to emit. Fortunately the webpack 4
    //optimization.splitChunks extracts the code that is common to more than one into "commonBundle.js".
    // The root file for each bundle should import errorHandler.ts to enable Bloom's custom
    // error handling for that web page.
    entry: {
        editTabRootBundle: "./bookEdit/editViewFrame.ts",
        readerSetupBundle:
            "./bookEdit/toolbox/readers/readerSetup/readerSetup.ts",
        editablePageBundle: "./bookEdit/editablePage.ts",
        bookPreviewBundle: "./bookPreview/bookPreview.ts",
        toolboxBundle: "./bookEdit/toolbox/toolboxBootstrap.ts",
        pageChooserBundle: "./pageChooser/page-chooser.ts",
        pageThumbnailListBundle:
            "./bookEdit/pageThumbnailList/pageThumbnailList.ts",
        pageControlsBundle:
            "./bookEdit/pageThumbnailList/pageControls/pageControls.tsx",
        publishUIBundle: globule.find(["./publish/**/*.tsx"]),
        enterpriseSettingsBundle: "./collection/enterpriseSettings.tsx",
        testBundle: globule.find([
            "./bookEdit/**/*Spec.ts",
            "./bookEdit/**/*Spec.js",
            "./lib/**/*Spec.ts",
            "./lib/**/*Spec.js",
            "./publish/**/*Spec.ts",
            "./publish/**/*Spec.js"
        ]),
        // These are referenced in pug files.
        audioRecording: "./bookEdit/toolbox/talkingBook/audioRecording.less",
        basePage: "./bookLayout/basePage.less",
        bloomDialog: "./bookEdit/css/bloomDialog.less",
        bookSettings: "./bookEdit/toolbox/bookSettings/bookSettings.less",
        editMode: "./bookEdit/css/editMode.less",
        editPaneGlobal: "./bookEdit/css/editPaneGlobal.less",
        motion: "./bookEdit/toolbox/motion/motion.less",
        music: "./bookEdit/toolbox/music/music.less",
        page_chooser: "./pageChooser/page-chooser.less",
        pageControls:
            "./bookEdit/pageThumbnailList/pageControls/pageControls.less",
        pageThumbnailList:
            "./bookEdit/pageThumbnailList/pageThumbnailList.less",
        rc_slider_bloom: "./bookEdit/css/rc-slider-bloom.less",
        readerSetup: "./bookEdit/toolbox/readers/readerSetup/readerSetup.less",
        readerTools: "./bookEdit/toolbox/readerTools.less",
        toolbox: "./bookEdit/toolbox/toolbox.less",
        // These are used programmatically by C# code.
        baseEPUB: "./publish/epub/baseEPUB.less",
        languageDisplay: "./bookLayout/languageDisplay.less",
        origami: "./bookEdit/css/origami.less",
        origamiEditing: "./bookEdit/css/origamiEditing.less",
        previewMode: "./bookPreview/previewMode.less",
        readerStyles: "./publish/android/readerStyles.less",
        previewMode: "./bookPreview/previewMode.less",
        topicChooser: "./bookEdit/TopicChooser/topicChooser.less",
        // referenced by Book Template and XMatter pug files
        ArithmeticTemplate:
            "./templates/template books/Arithmetic Template/ArithmeticTemplate.less",
        Basic_Book: "./templates/template books/Basic Book/Basic Book.less",
        Big_Book: "./templates/template books/Big Book/Big Book.less",
        Decodable_Reader:
            "./templates/template books/Decodable Reader/Decodable Reader.less",
        Special: "./templates/template books/Special/Special.less",
        configuration:
            "./templates/template books/Wall Calendar/configuration.less",
        wallCalendar:
            "./templates/template books/Wall Calendar/wallCalendar.less",
        Device_XMatter:
            "./templates/xMatter/Device-XMatter/Device-XMatter.less",
        Factory_XMatter:
            "./templates/xMatter/Factory-XMatter/Factory-XMatter.less",
        SIL_Cameroon_XMatter:
            "./templates/xMatter/SIL-Cameroon-Mothballed/SIL-Cameroon-XMatter.less",
        // The pug file uses Traditional-XMatter.css instead of SIL-PNG-XMatter.css.
        // SIL_PNG_XMatter: "./templates/xMatter/SIL-PNG-XMatter/SIL-PNG-XMatter.less",
        SuperPaperSaver_XMatter:
            "./templates/xMatter/SuperPaperSaver-XMatter/SuperPaperSaver-XMatter.less",
        TemplateStarter_XMatter:
            "./templates/xMatter/TemplateStarter-XMatter/TemplateStarter-XMatter.less",
        Traditional_XMatter:
            "./templates/xMatter/Traditional-XMatter/Traditional-XMatter.less",
        Video_XMatter: "./templates/xMatter/Video-XMatter/Video-XMatter.less",
        MXBBook_XMatter:
            "./templates/customXMatter/MXBBook-XMatter/MXBBook-XMatter.less",
        MBXPamphlet_XMatter:
            "./templates/customXMatter/MXBPamphlet-XMatter/MXBPamphlet-XMatter.less",
        Dari_XMatter:
            "./templates/customXMatter/Dari-XMatter/Dari-XMatter.less",
        Pashti_XMatter:
            "./templates/customXMatter/Pashti-XMatter/Pashti-XMatter.less"
    },

    output: {
        path: path.join(__dirname, outputDir),
        filename: "[name].js",

        libraryTarget: "var",

        //makes a single entry point module's epxorts accessible via Exports.
        //Note that if you include more than one entry point js in the frame, the second one will overwrite the Exports var
        // (see the other way of doing this, below, if that becomes necessary for some reason)
        // (JT: later: I think what the above means is that the root pug file for a browser control (or an iframe within it)
        // should not import more than commonBundle.js and then, after it, the one root bundle for that frame,
        // one of the bundles specified in the entry: block above. If you do import more than one, only the exports
        // from the LAST one will be accessible in FrameExports, since each bundle will set FrameExports and the
        // last one will win. I can't find the 'other way' of doing things if you need both lots of exports;
        // the preferred solution would be to reorganize the code so that each frame has only one root bundle.)
        library: "FrameExports"
    },

    resolve: {
        // For some reason, webpack began to complain about being given minified source.
        // alias: {
        //   "react-dom": pathToReactDom,
        //   react: pathToReact // the point of this is to use the minified version. https://christianalfoni.github.io/react-webpack-cookbook/Optimizing-rebundling.html
        // },
        modules: [
            ".",
            pathToOriginalJavascriptFilesInLib,
            node_modules,
            pathToBookEditJS,
            pathToOriginalJavascriptFilesInModified_Libraries
        ],
        extensions: [".js", ".jsx", ".ts", ".tsx", ".less"] //We may need to add .less here... otherwise maybe it will ignore them unless they are require()'d
    },
    plugins: [
        //answer on various legacy issues: http://stackoverflow.com/questions/28969861/managing-jquery-plugin-dependency-in-webpack?lq=1
        //prepend var $ = require("jquery") every time it encounters the global $ identifier or "jQuery".
        new webpack.ProvidePlugin({
            $: "jquery",
            jQuery: "jquery",
            "window.jQuery": "jquery"
        }),
        new MiniCssExtractPlugin({
            filename: "styles/[name].css",
            chunkFilename: "styles/[id].css"
        }),
        new ShellPlugin({
            onBuildEnd: {
                scripts: ["node cleanupCss.js"],
                blocking: false,
                parallel: false
            }
        })
    ],
    optimization: {
        minimize: false,
        namedModules: true,
        splitChunks: {
            cacheGroups: {
                default: false,
                commons: {
                    name: "commonBundle",
                    chunks: "initial",
                    // Our build process creates 10 independent bundle files.  (See exports.entry
                    // above.)  minChunks specifies how many of those bundles must contain a common
                    // chunk for that common chunk to be moved into commonBundle.js.  The default
                    // value 1 moves everything to a massive commonBundle.js, leaving only a small
                    // stub for each of the 10 original bundle files.  Specifying 10 creates the
                    // smallest commonBundle.js file, which is 6% smaller than the file created for
                    // webpack 1 using the old CommonChunkPlugin.  Specifying 9 (or 7 or 8) creates
                    // a 17% bigger commonBundle file at the cost of the smallest original bundle
                    // having access to some code it doesn't use.  This seemed like a good tradeoff.
                    minChunks: 9,
                    // This is the default value for minSize, the minimum size of a chunk to move
                    // to commonBundle.js.  Changing it didn't seem to have any effect in our build
                    // process.
                    minSize: 30000,
                    reuseExistingChunk: true
                }
            }
        }
    },
    module: {
        rules: [
            {
                test: /\.ts(x?)$/,
                use: [{ loader: "ts-loader" }]
            },
            {
                // For the most part, we're using typescript and ts-loader handles that.
                // But for things that are still in javascript, the following babel setup allows newer
                // javascript features by compiling to the version JS feature supported by the specific
                // version of FF we currently ship with.
                test: /\.(js|jsx)$/,
                exclude: [
                    /node_modules/,
                    /ckeditor/,
                    /jquery-ui/,
                    /-min/,
                    /qtip/,
                    /xregexp-all-min.js/
                ],
                use: [
                    {
                        loader: "babel-loader",
                        query: {
                            presets: [
                                // Ensure that we target our version of geckofx (mozilla/firefox)
                                [
                                    "babel-preset-env",
                                    {
                                        targets: {
                                            browsers: [
                                                "Firefox >= 45",
                                                "last 2 versions"
                                            ]
                                        }
                                    }
                                ],
                                "babel-preset-react"
                            ].map(localResolve)
                        }
                    }
                ]
            },
            {
                test: /\.less$/i,
                use: [
                    {
                        loader: "style-loader" // creates style nodes from JS strings
                    },
                    {
                        loader: "css-loader" // translates CSS into CommonJS
                    },
                    {
                        loader: "less-loader" // compiles Less to CSS
                    }
                ]
            },
            {
                test: /\.less$/,
                include: [
                    path.resolve(__dirname, "bookEdit/css/bloomDialog.less"),
                    path.resolve(__dirname, "bookEdit/css/editMode.less"),
                    path.resolve(__dirname, "bookEdit/css/editPaneGlobal.less"),
                    path.resolve(
                        __dirname,
                        "bookEdit/css/rc-slider-bloom.less"
                    ),
                    path.resolve(
                        __dirname,
                        "bookEdit/pageThumbnailList/pageControls/pageControls.less"
                    ),
                    path.resolve(
                        __dirname,
                        "bookEdit/pageThumbnailList/pageThumbnailList.less"
                    ),
                    path.resolve(
                        __dirname,
                        "bookEdit/toolbox/bookSettings/bookSettings.less"
                    ),
                    path.resolve(
                        __dirname,
                        "bookEdit/toolbox/motion/motion.less"
                    ),
                    path.resolve(
                        __dirname,
                        "bookEdit/toolbox/music/music.less"
                    ),
                    path.resolve(
                        __dirname,
                        "bookEdit/toolbox/readers/readerSetup/readerSetup.less"
                    ),
                    path.resolve(
                        __dirname,
                        "bookEdit/toolbox/readerTools.less"
                    ),
                    path.resolve(
                        __dirname,
                        "bookEdit/toolbox/talkingBook/audioRecording.less"
                    ),
                    path.resolve(__dirname, "bookEdit/toolbox/toolbox.less"),
                    path.resolve(__dirname, "bookLayout/basePage.less"),
                    path.resolve(__dirname, "pageChooser/page-chooser.less"),
                    path.resolve(__dirname, "publish/epub/baseEPUB.less"),
                    path.resolve(
                        __dirname,
                        "bookEdit/TopicChooser/topicChooser.less"
                    ),
                    path.resolve(__dirname, "bookPreview/previewMode.less"),
                    path.resolve(__dirname, "bookEdit/css/origami.less"),
                    path.resolve(__dirname, "bookEdit/css/origamiEditing.less"),
                    path.resolve(__dirname, "bookLayout/languageDisplay.less"),
                    path.resolve(__dirname, "bookPreview/previewMode.less"),
                    path.resolve(
                        __dirname,
                        "publish/android/readerStyles.less"
                    ),
                    path.resolve(__dirname, "templates/")
                ],
                use: [
                    {
                        loader: MiniCssExtractPlugin.loader
                    },
                    {
                        loader: "css-loader" // translates CSS into CommonJS
                    },
                    {
                        loader: "less-loader" // compiles Less to CSS
                    }
                ]
            },
            {
                // this allows things like background-image: url("myComponentsButton.svg") and have the resulting path look for the svg in the stylesheet's folder
                test: /\.(svg|jpg|png|gif)$/,
                use: {
                    loader: "file-loader"
                }
            }
        ]
    }
};
