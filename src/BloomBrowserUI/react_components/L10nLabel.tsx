import * as React from "react";
import Typography, { TypographyProps } from "@material-ui/core/Typography";
import { useL10n } from "./l10nHooks";

interface IProps extends TypographyProps {
    english: string;
    l10nKey: string;
}

// Displays a Text field with the label localized (only works if label is a string).
export const L10nLabel: React.FunctionComponent<IProps> = props => {
    const label = useL10n(props.english, props.l10nKey);
    return <Typography {...props}>{label}</Typography>;
};
