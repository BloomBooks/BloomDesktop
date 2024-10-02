const path = require("path");
const { merge } = require("webpack-merge");
const pathToOriginalJavascriptFilesInLib = path.resolve(__dirname, "lib");
const pathToBookEditJS = path.resolve(__dirname, "bookEdit/js");
const pathToOriginalJavascriptFilesInModified_Libraries = path.resolve(
    __dirname,
    "modified_libraries"
);
const globule = require("globule");

//note: if you change this, change it in gulpfile.js & karma.conf.js as well
const outputDir = "../../output/browser";
const core = require("./webpack.core.js");

// Because our output directory does not have the same parent as our node_modules, we
// need to resolve the babel related presets (and plugins).  This mapping function was
// suggested at https://github.com/babel/babel-loader/issues/166.
function localResolve(preset) {
    return Array.isArray(preset)
        ? [require.resolve(preset[0]), preset[1]]
        : require.resolve(preset);
}

module.exports = merge(core, {
    // mode must be set to either "production" or "development" in webpack 4.
    // Webpack-common is intended to be 'required' by something that provides that.
    context: __dirname,
    //Bloom is not (yet) one webapp; it's actually a several loosely related ones.
    //So we have multiple "entry points" that we need to emit. Fortunately the webpack 4
    //optimization.splitChunks extracts the code that is common to more than one into "commonBundle.js".
    // The root file for each bundle should import errorHandler.ts to enable Bloom's custom
    // error handling for that web page.
    entry: {
        editTabBundle: "./bookEdit/editViewFrame.ts",
        readerSetupBundle:
            "./bookEdit/toolbox/readers/readerSetup/readerSetup.ts",
        editablePageBundle: "./bookEdit/editablePage.ts",
        // Surprisingly, this is still necessary with the React Preview to get previews
        // to NOT allow editing.
        bookPreviewBundle:
            "./collectionsTab/collectionsTabBookPane/bookPreview.ts",
        toolboxBundle: "./bookEdit/toolbox/toolboxBootstrap.ts",
        spreadsheetBundle: "./spreadsheet/spreadsheetBundleRoot.ts",
        pageThumbnailListBundle:
            "./bookEdit/pageThumbnailList/pageThumbnailList.tsx",
        pageControlsBundle:
            "./bookEdit/pageThumbnailList/pageControls/pageControls.tsx",
        accessibilityCheckBundle: globule.find([
            "./publish/accessibilityCheck/**/*.tsx"
        ]),
        enterpriseSettingsBundle: "./collection/enterpriseSettings.tsx",

        performanceLogBundle: "./performance/PerformanceLogPage.tsx",
        appBundle: "./app/App.tsx",
        testBundle: globule.find([
            "./bookEdit/**/*Spec.ts",
            "./bookEdit/**/*Spec.js",
            "./lib/**/*Spec.ts",
            "./lib/**/*Spec.js",
            "./publish/**/*Spec.ts",
            "./publish/**/*Spec.js",
            "./react_components/**/*Spec.ts",
            "./react_components/**/*.spec.ts",
            "./utils/**/*Spec.ts"
        ]),

        // These work with c# ReactControl:
        problemReportBundle: "./problemDialog/ProblemDialog.tsx",
        messageBoxBundle: "./utils/BloomMessageBox.tsx",
        bookMakingSettingsBundle: "./collection/bookMakingSettingsControl.tsx",
        progressDialogBundle: "./react_components/Progress/ProgressDialog.tsx",
        requiresBloomEnterpriseBundle:
            "./react_components/requiresBloomEnterprise.tsx",
        createTeamCollectionDialogBundle:
            "./teamCollection/CreateTeamCollection.tsx",
        teamCollectionSettingsBundle:
            "./teamCollection/TeamCollectionSettingsPanel.tsx",
        joinTeamCollectionDialogBundle:
            "./teamCollection/JoinTeamCollectionDialog.tsx",
        autoUpdateSoftwareDlgBundle:
            "./react_components/AutoUpdateSoftwareDialog.tsx",
        duplicateManyDlgBundle: "./bookEdit/duplicateManyDialog.tsx",
        copyrightAndLicenseBundle:
            "./bookEdit/copyrightAndLicense/CopyrightAndLicenseDialog.tsx",
        collectionsTabPaneBundle: "./collectionsTab/CollectionsTabPane.tsx",
        publishTabPaneBundle: "./publish/PublishTab/PublishTabPane.tsx"

        // This slowed down webpack a ton, because the way it works is that it 1st finds it all,
        // then it excludes node_modules
        // testBundle: globule.find(["./**/*Spec.ts", "./**/*Spec.js", "!./node_modules/**"])
    },

    output: {
        path: path.join(__dirname, outputDir),
        filename: "[name].js",

        libraryTarget: "var",

        // Makes a single entry point module's exports accessible via Exports.
        library: "[name]"
    },
    resolve: {
        // For some reason, webpack began to complain about being given minified source.
        // alias: { x
        //   "react-dom": pathToReactDom,
        //   react: pathToReact // the point of this is to use the minified version. https://christianalfoni.github.io/react-webpack-cookbook/Optimizing-rebundling.html
        // },
        modules: [
            ".",
            pathToOriginalJavascriptFilesInLib,
            "node_modules",
            pathToBookEditJS,
            pathToOriginalJavascriptFilesInModified_Libraries
        ],
        extensions: [".js", ".jsx", ".ts", ".tsx"] //We may need to add .less here... otherwise maybe it will ignore them unless they are require()'d
    },
    optimization: {
        minimize: false,
        moduleIds: "named",
        splitChunks: {
            cacheGroups: {
                default: false,
                commons: {
                    name: "commonBundle",
                    chunks: "initial",
                    // Our build process creates multiple independent bundle files.  (See exports.entry
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
                // For the most part, we're using typescript and ts-loader handles that.
                // But for things that are still in javascript, the following babel setup allows newer
                // javascript features by compiling to the version JS feature supported by the specific
                // version of WebView2/Edge we currently target.
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
                        options: {
                            presets: [
                                // Ensure that we target our minimum version of WebView2/Edge.
                                // (See Program.IsWebviewMissingOrTooOld)
                                [
                                    "@babel/preset-env",
                                    {
                                        targets: {
                                            edge: "112"
                                        }
                                    }
                                ],
                                "@babel/preset-react"
                            ].map(localResolve)
                        }
                    }
                ]
            },
            // WOFF Font
            {
                test: /\.woff(\?v=\d+\.\d+\.\d+)?$/,
                use: {
                    loader: "url-loader",
                    options: {
                        limit: 10000,
                        mimetype: "font/woff"
                    }
                }
            }
        ]
    }
});
