import { writeFileSync, readFileSync } from "fs";

// This file implements the package.json command build:pageSizes, which creates
// bookLayout/page-size-mixin.less from a DistFiles/pageSizes.json
interface PageSize {
    size: string;
    width: string;
    height: string;
}

var input = readFileSync("../../DistFiles/pageSizes.json", "utf8");
var sizes = JSON.parse(input);
var data = "";
for (var item of sizes.sizes) {
    const pageSize: PageSize = item as PageSize;
    data += "@" + pageSize.size + "-Height: " + pageSize.height + ";\n";
    data += "@" + pageSize.size + "-Width: " + pageSize.width + ";\n";
}

writeFileSync("bookLayout/page-size-mixin.less", data);
