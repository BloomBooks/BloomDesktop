// More than one process compiles our code, and each may have its own webpack needs as far as output.
// They all include this file, which is just the rules needed to actually compile the code.
// Note: There is more in webpack.common.js which *might* be needed here eventually, it's unclear.
// As we run into things that compile fine for the whole build but not in a situation like storybook,
// then we can move those webpack rules here.
var path = require("path");
var webpack = require("webpack");
var WebpackBuildNotifierPlugin = require("webpack-build-notifier");

function NothingPlugin() {
    this.apply = function () {};
}

module.exports = {
    module: {
        rules: [
            {
                test: /\.(ts|tsx)$/,
                exclude: [
                    /[\\/]component-tester[\\/]/,
                    /[\\/]component-tests[\\/]/,
                ],
                use: [
                    {
                        loader: require.resolve("ts-loader"),
                    },
                ],
            },
            {
                test: /\.less$/i,
                use: [
                    {
                        loader: "style-loader", // creates style nodes from JS strings
                    },
                    {
                        loader: "css-loader", // translates CSS into CommonJS,
                        options: {
                            url: {
                                filter: (url, resourcePath) => {
                                    // Don't let webpack resolve /bloom/ urls. Just leave them as is.
                                    return !url.startsWith("/bloom/");
                                },
                            },
                        },
                    },
                    {
                        loader: "less-loader", // compiles Less to CSS
                    },
                ],
            },
            {
                test: /\.css$/,
                use: ["style-loader", "css-loader"],
            },
            {
                // this allows things like background-image: url("myComponentsButton.svg") and have the resulting path look for the svg in the stylesheet's folder
                // the last few seem to be needed for (at least) slick-carousel to build.
                test: /\.(svg|jpg|png|ttf|eot|gif)$/,
                use: {
                    loader: "file-loader",
                },
            },
            {
                test: /\.mp3$/,
                type: "asset/resource",
            },
            {
                test: /react-spring/,
                // work-around for https://github.com/plouc/nivo/issues/1290 until it gets fixed.
                sideEffects: true,
            },
        ],
    },

    plugins: [
        //answer on various legacy issues: http://stackoverflow.com/questions/28969861/managing-jquery-plugin-dependency-in-webpack?lq=1
        //prepend var $ = require("jquery") every time it encounters the global $ identifier or "jQuery".
        new webpack.ProvidePlugin({
            $: "jquery",
            jQuery: "jquery",
            "window.jQuery": "jquery",
        }),
        // Ignore component-tester stuff
        new webpack.IgnorePlugin({
            resourceRegExp: /[\\/](component-tester|component-tests)[\\/]/,
        }),
        // don't include the notifier when building on server, which uses production
        process.env.NODE_ENV === "debug"
            ? new WebpackBuildNotifierPlugin({})
            : new NothingPlugin(),
    ],
    resolve: {
        extensions: [".ts", ".tsx"],

        // Starting with emotion 11, we started needing these -- we don't understand why.
        // Without them, when the emotion files try to resolve the packages,
        // the `@emotion/` versions of the packages will be found instead.
        // I.e. in the emotion file, `import x from 'react'` will look for x
        // in node_modules/@emotion/react instead of node_modules/react.
        alias: {
            react: path.resolve(__dirname, "node_modules/react"),
            stylis: path.resolve(__dirname, "node_modules/stylis"),
        },
    },
};
