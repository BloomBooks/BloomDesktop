// Storybook notices this file in this location and uses it
// See https://storybook.js.org/docs/configurations/custom-webpack-config/

const bloomCompilationCoreStuff = require("../webpack.core.js");
const merge = require("webpack-merge");

// Here `config` is storybook's own webpack settings. We want to combine those
// with what Bloom requires
module.exports = async ({ config }) => merge(config, bloomCompilationCoreStuff);

//nb: the docs show the following alternative, but the above seems more robust for the future?
// module.exports = async ({ config, mode }) => {
//  return { ...config, module: { ...config.module, rules: custom.module.rules } };
//};
