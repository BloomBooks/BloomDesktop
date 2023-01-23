/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import Typography from "@material-ui/core/Typography";
import * as React from "react";
import { ThemeProvider, useTheme, createTheme } from "@material-ui/core/styles";
import "./statusPanelCommon.less"; // Now we have .less and emotion going here. Someday we should unify.
import { StatusPanelState } from "./TeamCollectionBookStatusPanel";
import { IconHeadingBodyMenuPanel } from "../react_components/iconHeadingBodyMenuPanel";

export interface IStatusPanelProps {
    lockState: StatusPanelState;
    title: string;
    subTitle: string;
    icon: JSX.Element;
    button?: JSX.Element;
    children?: JSX.Element;
    menu?: JSX.Element; // when book is checked out, About my Avatar... and Forget Changes and Check in Book
    className?: string; // additional class for  root div; enables emotion CSS.
}

export const StatusPanelCommon: React.FunctionComponent<IStatusPanelProps> = (
    props: IStatusPanelProps
) => {
    const outerTheme = useTheme();
    const buttonColor =
        props.lockState === "lockedByMe" || props.lockState === "needsReload"
            ? outerTheme.palette.warning.main
            : outerTheme.palette.primary.main;
    const buttonTheme = createTheme({
        palette: {
            primary: {
                main: buttonColor
            },
            action: {
                // Yagni: currently the only time we disable a button is when using the warning color
                // If we ever disable the other version, we probably want primary.dark here.
                disabledBackground: outerTheme.palette.warning.dark
            }
        }
    });
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
                <ThemeProvider theme={buttonTheme}>
                    <div className="panel-button">{props.button}</div>
                </ThemeProvider>
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
