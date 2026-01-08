import * as React from "react";
import { TextFieldProps } from "@mui/material/TextField";
import { createTheme, ThemeProvider, useTheme } from "@mui/material/styles";
import { useState } from "react";
import { useDrawAttention } from "./UseDrawAttention";
import { ILocalizationProps } from "./l10nComponents";
import { MuiTextField } from "./muiTextField";
import { kBloomGold } from "../bloomMaterialUITheme";

export const AttentionTextField: React.FunctionComponent<
    {
        submitAttempts: number;
        value: string;
        isValid: (value: string) => boolean;
        onChange: (value: string) => void;
        label: string;
    } & ILocalizationProps &
        Omit<TextFieldProps, "onChange" | "value" | "error">
> = (props) => {
    const {
        submitAttempts,
        value,
        isValid,
        onChange,
        className,
        ...muiTextFieldProps
    } = props;
    const [isValueValid, setIsValueValid] = useState(false);

    const parentTheme = useTheme();

    // Default to showing invalid fields as yellow
    // Enhance: make it easier for parent components to override this?
    const yellowErrorTheme = React.useMemo(
        () =>
            createTheme({
                ...parentTheme,
                palette: {
                    ...parentTheme.palette,
                    error: {
                        ...parentTheme.palette.error,
                        main: kBloomGold,
                    },
                },
            }),
        [parentTheme],
    );

    React.useEffect(() => {
        setIsValueValid(isValid(value));
    }, [value, isValid]);

    const attentionClass = useDrawAttention(submitAttempts, () => isValueValid);

    return (
        <ThemeProvider theme={yellowErrorTheme}>
            <MuiTextField
                className={(className ? className + " " : "") + attentionClass}
                error={
                    (value && value.length > 0 && !isValueValid) ||
                    (submitAttempts > 0 && !isValueValid)
                }
                onChange={(event) => {
                    onChange(event.target.value);
                }}
                value={value}
                {...muiTextFieldProps}
            />
        </ThemeProvider>
    );
};
