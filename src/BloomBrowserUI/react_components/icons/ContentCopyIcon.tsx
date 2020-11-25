import React = require("react");
import SvgIcon from "@material-ui/core/SvgIcon";

// I pulled this icon from www.materialui.co
// (not the official material-ui site, which doesn't have what we want).
export const ContentCopyIcon = props => (
    <SvgIcon {...props}>
        <path
            d="M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z"
            fill="#96668f"
        />
    </SvgIcon>
);

export default ContentCopyIcon;
