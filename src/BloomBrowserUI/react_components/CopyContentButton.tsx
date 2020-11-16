import * as React from "react";
import { useState } from "react";
import IconButton from "@material-ui/core/IconButton";
import Zoom from "@material-ui/core/Zoom";
import Typography from "@material-ui/core/Typography";
import ContentCopyIcon from "./icons/ContentCopyIcon";
import { useL10n } from "./l10nHooks";

export interface ICopyContentButtonProps {
    onClick: () => void;
}

const CopyContentButton: React.FC<ICopyContentButtonProps> = props => {
    const copiedText = useL10n("Copied", "EditTab.SourceBubbleCopied");
    const [showTransition, setShowTransition] = useState(false);

    const transitionDuration = 300; // Length of time to transition "copied" message in or out
    const feedbackDuration = 1000; // Length of time "copied" message stays "up"

    const handleClick = () => {
        setShowTransition(true);
        props.onClick();
    };

    const handleOnEntered = () => {
        setTimeout(() => {
            setShowTransition(false);
        }, feedbackDuration);
    };

    return (
        <div className="bloom-ui source-copy-button">
            <Zoom
                in={showTransition}
                enter={true}
                exit={true}
                timeout={transitionDuration}
                onEntered={handleOnEntered}
            >
                <div className="copy-transition">
                    <Typography>{copiedText}</Typography>
                </div>
            </Zoom>
            <IconButton aria-label="copy" size="small" onClick={handleClick}>
                <ContentCopyIcon />
            </IconButton>
        </div>
    );
};

export default CopyContentButton;
