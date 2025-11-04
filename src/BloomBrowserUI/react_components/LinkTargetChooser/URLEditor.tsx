import * as React from "react";
import { useState } from "react";
import { css } from "@emotion/react";
import { Box, TextField, IconButton } from "@mui/material";
import ContentPasteIcon from "@mui/icons-material/ContentPaste";
import OpenInNewIcon from "@mui/icons-material/OpenInNew";
import { BloomTooltip } from "../BloomToolTip";
import { get, post } from "../../utils/bloomApi";

export const URLEditor: React.FunctionComponent<{
    currentURL: string;
    onChange: (url: string) => void;
}> = (props) => {
    const [url, setURL] = useState(props.currentURL);

    React.useEffect(() => {
        setURL(props.currentURL);
    }, [props.currentURL]);

    const handleURLChange = (newURL: string) => {
        setURL(newURL);
        props.onChange(newURL);
    };

    const handlePaste = async () => {
        try {
            const result = await get("common/clipboardText");
            if (result.data) {
                handleURLChange(result.data);
            }
        } catch (error) {
            console.error("Failed to get clipboard text:", error);
        }
    };

    const handleOpenInBrowser = () => {
        if (url) {
            // Use the Bloom API to open the URL in the default browser
            post(`common/openUrl?url=${encodeURIComponent(url)}`);
        }
    };

    return (
        <Box
            css={css`
                display: flex;
                align-items: center;
                gap: 8px;
            `}
        >
            <TextField
                fullWidth
                size="small"
                value={url}
                onChange={(e) => handleURLChange(e.target.value)}
                placeholder="Paste or enter a URL"
                variant="outlined"
                css={css`
                    flex: 1;
                `}
                data-testid="url-input"
            />
            <BloomTooltip tip="Paste from clipboard">
                <IconButton
                    onClick={handlePaste}
                    size="small"
                    css={css`
                        color: #1976d2;
                    `}
                    data-testid="paste-button"
                >
                    <ContentPasteIcon />
                </IconButton>
            </BloomTooltip>
            <BloomTooltip tip="Open in browser">
                <IconButton
                    onClick={handleOpenInBrowser}
                    size="small"
                    disabled={!url}
                    css={css`
                        color: #1976d2;
                        &:disabled {
                            color: rgba(0, 0, 0, 0.26);
                        }
                    `}
                    data-testid="open-button"
                >
                    <OpenInNewIcon />
                </IconButton>
            </BloomTooltip>
        </Box>
    );
};
