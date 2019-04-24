// More than one process compiles our code, and each may have its own webpack needs as far as output.
// They all include this file, which is just the rules needed to actually compile the code.

var webpack = require("webpack");

module.exports = ({ config }) => {
    config.module.rules.push(
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
    );
    config.plugins.push(
        //answer on various legacy issues: http://stackoverflow.com/questions/28969861/managing-jquery-plugin-dependency-in-webpack?lq=1
        //prepend var $ = require("jquery") every time it encounters the global $ identifier or "jQuery".
        new webpack.ProvidePlugin({
            $: "jquery",
            jQuery: "jquery",
            "window.jQuery": "jquery"
        })
    );
    config.resolve.extensions.push(".ts", ".tsx");
    return config;
};
