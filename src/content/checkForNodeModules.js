const fs = require("fs");
if (!fs.existsSync("./node_modules")) {
    throw new Error(
        "Error: node_modules directory missing in the `content` directory. Please run yarn there.",
    );
}
