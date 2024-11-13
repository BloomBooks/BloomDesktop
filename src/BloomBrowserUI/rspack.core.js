import path from "path";

function NothingPlugin() {
    this.apply = function() {};
}

export default {
    module: {
        rules: [
            {
                test: /\.(ts|tsx)$/,
                type: "typescript"
            },
            {
                test: /\.less$/i,
                type: "css", // Rspack handles Less out of the box
                use: {
                    lessOptions: {
                        javascriptEnabled: true
                    }
                }
            },
            {
                test: /\.css$/,
                type: "css"
            },
            {
                test: /\.(svg|jpg|png|ttf|eot|gif)$/,
                type: "asset/resource"
            }
        ]
    },

    builtins: {
        provide: {
            $: "jquery",
            jQuery: "jquery",
            "window.jQuery": "jquery"
        }
    },

    resolve: {
        extensions: [".ts", ".tsx"],
        alias: {
            react: path.resolve("./node_modules/react"),
            stylis: path.resolve("./node_modules/stylis")
        }
    }
};
