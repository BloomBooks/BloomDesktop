import { css } from "@emotion/react";

import Typography from "@mui/material/Typography";
import * as React from "react";
import {
    ThemeProvider,
    Theme,
    StyledEngineProvider,
    useTheme,
    createTheme,
    adaptV4Theme,
} from "@mui/material/styles";
import "./statusPanelCommon.less"; // Now we have .less and emotion going here. Someday we should unify.
import { IconHeadingBodyMenuPanel } from "../react_components/iconHeadingBodyMenuPanel";

declare module "@mui/styles/defaultTheme" {
    // eslint-disable-next-line @typescript-eslint/no-empty-object-type, @typescript-eslint/no-empty-interface
    interface DefaultTheme extends Theme {}
}

export interface IStatusPanelProps {
    title: string;
    subTitle: string;
    icon: JSX.Element;
    button?: JSX.Element;
    useWarningColorForButton?: boolean;
    children?: JSX.Element;
    menu?: JSX.Element; // when book is checked out, About my Avatar... and Forget Changes and Check in Book
    className?: string; // additional class for  root div; enables emotion CSS.
}

export const StatusPanelCommon: React.FunctionComponent<IStatusPanelProps> = (
    props: IStatusPanelProps,
) => {
    const outerTheme = useTheme();
    const buttonColor = props.useWarningColorForButton
        ? outerTheme.palette.warning.main
        : outerTheme.palette.primary.main;
    const buttonTheme = createTheme(
        adaptV4Theme({
            palette: {
                primary: {
                    main: buttonColor,
                },
                action: {
                    // Yagni: currently the only time we disable a button is when using the warning color
                    // If we ever disable the other version, we probably want primary.dark here.
                    disabledBackground: outerTheme.palette.warning.dark,
                },
            },
        }),
    );
    // Get button to inherit outerTheme typography.
    // Review: I couldn't find a way to clone a theme. I also tried creating a button theme and changing
    // the primary palette with a useEffect when lockState changed, but it didn't work for some reason.
    buttonTheme.typography = outerTheme.typography;

    return (
        <div
            className={
                "status-panel" + (props.className ? " " + props.className : "")
            }
        >
            <IconHeadingBodyMenuPanel
                heading={props.title}
                body={props.subTitle}
                icon={props.icon}
                menu={props.menu}
            />
            <div
                css={css`
                    flex-direction: row;
                    flex: 1;
                    justify-content: flex-end;
                    align-items: flex-end;
                    display: flex;
                    margin-bottom: 15px;
                `}
            >
                {props.children && (
                    <div
                        css={css`
                            display: flex;
                            flex: 1;
                        `}
                        className="panel-children"
                    >
                        {props.children}
                    </div>
                )}
                <StyledEngineProvider injectFirst>
                    <ThemeProvider theme={buttonTheme}>
                        <div className="panel-button">{props.button}</div>
                    </ThemeProvider>
                </StyledEngineProvider>
            </div>
        </div>
    );
};

export const getLockedInfoChild = (info: string) => (
    <div className="lockedInfo">
        <Typography align="left" variant="subtitle2">
            {info}
        </Typography>
    </div>
);
