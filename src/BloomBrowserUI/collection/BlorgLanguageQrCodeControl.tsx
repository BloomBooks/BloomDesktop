import { css } from "@emotion/react";

import * as React from "react";
import { useApiStringWithUpdate } from "../utils/bloomApi";
import { TextField, Typography } from "@mui/material";
import { useL10n } from "../react_components/l10nHooks";
import { ApiCheckbox } from "../react_components/ApiCheckbox";

const BlorgLanguageQrCodeControl: React.FunctionComponent = () => {
    const qrCodeLabel = useL10n(
        "QR code in Bloom badge",
        "CollectionSettingsDialog.BookMakingTab.QrCodeLabel",
    );

    const [badgeLabel, setBadgeLabel] = useApiStringWithUpdate(
        "settings/badgeQrCodeLabel",
        "",
    );

    const badgeTextLabel = useL10n(
        "Label for QR code badge",
        "CollectionSettingsDialog.BookMakingTab.QrCodeBadgeLabelCaption",
    );

    return (
        <div>
            <Typography
                css={css`
                    font-weight: 700 !important;
                    margin-top: 10px;
                `}
            >
                {qrCodeLabel}
            </Typography>
            <ApiCheckbox
                label="Show QR code for current language books"
                l10nKey="CollectionSettingsDialog.BookMakingTab.QrCodeShowLanguage"
                apiEndpoint="settings/showBlorgLanguageQrCode"
            />
            <TextField
                id="qr-code-badge-label"
                variant="outlined"
                label={badgeTextLabel}
                value={badgeLabel}
                onChange={(ev) => setBadgeLabel(ev.target.value)}
                multiline
                minRows={2}
                fullWidth
                css={css`
                    margin-top: 12px;
                    .MuiInputBase-root {
                        align-items: flex-start;
                        padding: 0 5px;
                    }
                    .MuiInputBase-input {
                        padding-top: 6px;
                        padding-bottom: 6px;
                        line-height: 1.3;
                    }
                `}
            ></TextField>
        </div>
    );
};

export default BlorgLanguageQrCodeControl;
