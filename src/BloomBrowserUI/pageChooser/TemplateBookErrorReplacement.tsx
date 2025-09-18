import { css } from "@emotion/react";

import * as React from "react";
import Typography from "@mui/material/Typography";
import { useL10n } from "../react_components/l10nHooks";

interface ITemplateBookErrorReplacementProps {
    templateBookPath: string;
}

// This component replaces the usual Title/template page thumbnails group, when we can't find the template
// book that our JSON input refers to.
export const TemplateBookErrorReplacement: React.FunctionComponent<
    ITemplateBookErrorReplacementProps
> = (props) => {
    const path = props.templateBookPath;
    const index = path.lastIndexOf("/");
    const templateName = path.substring(index + 1, path.length);
    const templateTitle = templateName.replace(".html", "").replace(".htm", "");
    const captionKey = "TemplateBooks.PageLabel." + templateTitle;
    const groupCaption = useL10n(templateTitle, captionKey);

    const message = useL10n(
        "Could not find {0}",
        "EditTab.AddPageDialog.NoTemplate",
        "Seen when the book's main template page file is missing.",
        props.templateBookPath,
    );

    return (
        <div
            css={css`
                margin-left: 30px;
            `}
        >
            <Typography
                variant="h6"
                css={css`
                    font-weight: bold !important;
                    display: block;
                `}
            >
                {groupCaption}
            </Typography>
            <Typography
                css={css`
                    margin-left: 20px !important; // Typography specifies 0 margin.
                `}
            >
                {message}
            </Typography>
        </div>
    );
};

export default TemplateBookErrorReplacement;
