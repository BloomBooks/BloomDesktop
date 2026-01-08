import { css } from "@emotion/react";
import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import Typography from "@mui/material/Typography";
import { Div } from "../../react_components/l10nComponents";
import { kBloomBlue, kBannerGray } from "../../bloomMaterialUITheme";

export const PublishScreenBanner: React.FunctionComponent<{
    titleEnglish: string;
    titleL10nId: string;
    descriptionMarkdown?: string;
    descriptionL10nId?: string;
}> = (props) => {
    const localizedTitle = useL10n(props.titleEnglish, props.titleL10nId);

    return (
        <div
            id="publishScreenBanner"
            css={css`
                display: flex;
                flex-direction: row;
                justify-content: space-between;
                background-color: ${kBannerGray};
                padding: 1rem;
            `}
        >
            <div
                css={css`
                    display: flex;
                    flex-direction: column;
                `}
            >
                <Typography
                    css={css`
                        font-weight: bold !important;
                        padding-bottom: 0.25rem;
                    `}
                    variant="h4"
                >
                    {localizedTitle}
                </Typography>
                {props.descriptionL10nId && (
                    <Div
                        css={css`
                            font-size: 9pt;
                            max-width: 800px; // limit line-length on large monitors
                            a {
                                color: ${kBloomBlue};
                            }
                        `}
                        l10nKey={props.descriptionL10nId}
                    >
                        {props.descriptionMarkdown}
                    </Div>
                )}
            </div>
            <div
                css={css`
                    display: flex;
                    flex-direction: row;
                `}
            >
                {props.children}
            </div>
        </div>
    );
};

export default PublishScreenBanner;
