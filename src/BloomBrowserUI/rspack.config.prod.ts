import common from "./rspack.config";
import { cleverMerge } from "@rspack/core";

export default cleverMerge(common, {
    mode: "production",
    devtool: "source-map"
});
