// More than one process compiles our code, and each may have its own webpack needs as far as output.
// They all include this file, which is just the rules needed to actually compile the code.
// Note: There is more in webpack.common.js which *might* be needed here eventually, it's unclear.
// As we run into things that compile fine for the whole build but not in a situation like storybook,
// then we can move those webpack rules here.

var webpack = require("webpack");

module.exports = {
    module: {
        rules: [
            {
                test: /\.(ts|tsx)$/,
                use: [
                    {
                        loader: require.resolve("ts-loader")
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
                test: /\.css$/,
                loader: "style-loader!css-loader"
            }
        ]
    },

    plugins: [
        //answer on various legacy issues: http://stackoverflow.com/questions/28969861/managing-jquery-plugin-dependency-in-webpack?lq=1
        //prepend var $ = require("jquery") every time it encounters the global $ identifier or "jQuery".
        new webpack.ProvidePlugin({
            $: "jquery",
            jQuery: "jquery",
            "window.jQuery": "jquery"
        })
    ],
    resolve: {
        extensions: [".ts", ".tsx"]
    }
};
