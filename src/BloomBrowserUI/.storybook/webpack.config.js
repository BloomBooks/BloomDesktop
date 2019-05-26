// Storybook notices this file in this location and uses it
// See https://storybook.js.org/docs/configurations/custom-webpack-config/

const bloomCompilationCoreStuff = require("../webpack.core.js");
const merge = require("webpack-merge");

// Here `config` is storybook's own webpack settings. We want to combine those
// with what Bloom requires
module.exports = async ({ config }) => {
    // hack to fix svg handling in storybook 5.0. Should be fixed for 5.1
    // https://github.com/storybooks/storybook/issues/5708#issuecomment-467364602
    // remove the "svg" from the webpack file-loader, which had;
    // test: /\.(svg|ico|jpg|jpeg|png|gif|eot|otf|webp|ttf|woff|woff2|cur|ani)(\?.*)?$/,
    config.module.rules.forEach(function(data, key) {
        if (data.test.toString().indexOf("svg|") >= 0) {
            // remove svg from the loader of storybook, so that Bloom's webpack can handle it
            config.module.rules[key].test = data.test
                .toString()
                .replace("svg|", "");
            return false;
        }
    });
    return merge(config, bloomCompilationCoreStuff);
};

//nb: the docs show the following alternative, but the above seems more robust for the future?
// module.exports = async ({ config, mode }) => {
//  return { ...config, module: { ...config.module, rules: custom.module.rules } };
//};
