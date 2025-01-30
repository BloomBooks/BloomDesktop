/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { Typography } from "@mui/material";
import { useL10n } from "../../../react_components/l10nHooks";

export const OverlayKeyHints: React.FC = () => {
    // We no longer use alt and ctrl keys for resizing and moving overlays.
    // Keeping the code around for now in case we want some similar help.
    // const dragHint = useL10n(
    //     "drag",
    //     "EditTab.Toolbox.OverlayTool.KeyHints.Drag"
    // );
    // const resizeHint = useL10n(
    //     "resize",
    //     "EditTab.Toolbox.OverlayTool.KeyHints.Resize"
    // );
    // const moveHint = useL10n(
    //     "move",
    //     "EditTab.Toolbox.OverlayTool.KeyHints.Move"
    // );
    // const rowRule = `display: flex; flex-direction: row; p {font-size: 0.75rem !important;}`;
    // const imgRule = `margin-right: 4px;`;

    return null;
    //(
    // <div
    //     css={css`
    //         display: flex;
    //         flex-direction: column;
    //         margin-bottom: 8px;
    //     `}
    // >
    //     <div
    //         css={css`
    //             ${rowRule}
    //         `}
    //     >
    //         <img
    //             css={css`
    //                 ${imgRule}
    //             `}
    //             src="/bloom/bookEdit/toolbox/overlay/AltKey.svg"
    //         />
    //         <Typography>{`+ ${dragHint} = ${resizeHint}`}</Typography>
    //     </div>
    //     <div
    //         css={css`
    //             ${rowRule}
    //         `}
    //     >
    //         <img
    //             css={css`
    //                 ${imgRule}
    //             `}
    //             src="/bloom/bookEdit/toolbox/overlay/CtrlKey.svg"
    //         />
    //         <Typography>{`+ ${dragHint} = ${moveHint}`}</Typography>
    //     </div>
    // </div>
    //);
};
