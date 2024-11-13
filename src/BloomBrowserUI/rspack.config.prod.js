import rspack from "@rspack/core";
const { merge } = rspack;
import common from "./rspack.common.js";

export default merge(common, {
    mode: "production",
    devtool: "source-map"
});
