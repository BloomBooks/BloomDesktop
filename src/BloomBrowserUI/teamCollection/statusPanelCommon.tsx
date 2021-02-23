import Typography from "@material-ui/core/Typography";
import * as React from "react";
import {
    ThemeProvider,
    useTheme,
    createMuiTheme
} from "@material-ui/core/styles";
import "./statusPanelCommon.less";
import { LockState } from "./TeamCollectionBookStatusPanel";

export interface IStatusPanelProps {
    lockState: LockState;
    title: string;
    subTitle: string;
    icon: JSX.Element;
    button?: JSX.Element;
    children?: JSX.Element;
    menu?: JSX.Element; // when book is checked out, About my Avatar... and Forget Changes and Check in Book
}

export const StatusPanelCommon: React.FunctionComponent<IStatusPanelProps> = (
    props: IStatusPanelProps
) => {
    const outerTheme = useTheme();
    const buttonColor =
        props.lockState === "lockedByMe"
            ? outerTheme.palette.warning.main
            : outerTheme.palette.primary.main;
    const buttonTheme = createMuiTheme({
        palette: {
            primary: {
                main: buttonColor
            }
        }
    });
    // Get button to inherit outerTheme typography.
    // Review: I couldn't find a way to clone a theme. I also tried creating a button theme and changing
    // the primary palette with a useEffect when lockState changed, but it didn't work for some reason.
    buttonTheme.typography = outerTheme.typography;

    return (
        <div className="status-panel">
            <div className="panel-top">
                <div className="icon-or-avatar">{props.icon}</div>
                <div className="panel-titles">
                    <Typography
                        className="main-title"
                        align="left"
                        variant="h6"
                    >
                        {props.title}
                    </Typography>
                    <Typography
                        className="sub-title"
                        align="left"
                        variant="subtitle2"
                    >
                        {props.subTitle}
                    </Typography>
                </div>
                <div className="status-panel-menu">{props.menu}</div>
            </div>
            {props.children && (
                <div className="panel-children">{props.children}</div>
            )}
            <ThemeProvider theme={buttonTheme}>
                <div className="panel-button">{props.button}</div>
            </ThemeProvider>
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
