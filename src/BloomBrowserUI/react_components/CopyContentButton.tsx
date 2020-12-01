import * as React from "react";
import { useState } from "react";
import IconButton from "@material-ui/core/IconButton";
import Fade from "@material-ui/core/Fade";
import Typography from "@material-ui/core/Typography";
import ContentCopyIcon from "./icons/ContentCopyIcon";
import { useL10n } from "./l10nHooks";

export interface ICopyContentButtonProps {
    onClick: () => void;
}

const CopyContentButton: React.FC<ICopyContentButtonProps> = props => {
    const copiedText = useL10n("Copied", "EditTab.SourceBubbleCopied");
    const copyTooltip = useL10n("Copy", "Common.Copy");
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
        <div className="bloom-ui source-copy-button" title={copyTooltip}>
            <Fade
                in={showTransition}
                enter={true}
                exit={true}
                timeout={transitionDuration}
                onEntered={handleOnEntered}
            >
                <div className="copy-transition">
                    <Typography>{copiedText}</Typography>
                </div>
            </Fade>
            <IconButton aria-label="copy" size="small" onClick={handleClick}>
                <ContentCopyIcon />
            </IconButton>
        </div>
    );
};

export default CopyContentButton;
