import * as React from "react";
import "./DeviceFrame.less";
import { useState, useEffect } from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { useDrawAttention } from "../../react_components/UseDrawAttention";
import { useTheme, Theme, Typography } from "@material-ui/core";
import IconButton from "@material-ui/core/IconButton";
import RefreshIcon from "@material-ui/icons/Refresh";

/*
  Example usage:
  <DeviceFrame defaultLandscape={true}>
    <iframe..../>
  </DeviceFrame>
*/

export const DeviceAndControls: React.FunctionComponent<{
    defaultLandscape: boolean;
    canRotate: boolean;
    url: string;
    iframeClass?: string;
    showRefresh?: boolean;
    highlightRefreshIcon?: boolean;
    onRefresh?: () => void;
}> = props => {
    const [landscape, setLandscape] = useState(props.defaultLandscape);
    const theme: Theme = useTheme();
    useEffect(() => {
        setLandscape(props.defaultLandscape);
    }, [props]);
    const refreshText = useL10n("Refresh", "Common.Refresh");

    const attentionClass = useDrawAttention(
        props.highlightRefreshIcon ? 1 : 0,
        () => {
            return !props.highlightRefreshIcon;
        }
    );

    return (
        <div className="deviceAndControls">
            <div
                className={
                    "deviceFrame fullSize " +
                    (landscape ? "landscape" : "portrait")
                }
            >
                <iframe
                    title="book preview"
                    src={props.url}
                    className={props.iframeClass}
                />
            </div>
            {props.canRotate && (
                <>
                    <OrientationButton
                        selected={!landscape}
                        landscape={false}
                        onClick={() => setLandscape(false)}
                    />
                    <OrientationButton
                        selected={landscape}
                        landscape={true}
                        onClick={() => setLandscape(true)}
                    />
                </>
            )}
            {props.showRefresh && (
                <div
                    className={
                        "refresh-button-row" +
                        (landscape ? " landscape" : "") +
                        (props.canRotate ? " with-orientation-buttons" : "")
                    }
                    onClick={() => props.onRefresh && props.onRefresh()}
                >
                    <IconButton
                        aria-label="refresh preview"
                        className={"refresh-icon " + attentionClass}
                    >
                        <RefreshIcon
                            fontSize="large"
                            htmlColor={
                                props.highlightRefreshIcon
                                    ? theme.palette.primary.main
                                    : theme.palette.text.secondary
                            }
                        />
                    </IconButton>
                    <Typography
                        color={
                            props.highlightRefreshIcon
                                ? "primary"
                                : "textSecondary"
                        }
                    >
                        {refreshText}
                    </Typography>
                </div>
            )}
        </div>
    );
};

const OrientationButton: React.FunctionComponent<{
    landscape: boolean;
    selected: boolean;
    onClick: (landscape: boolean) => void;
}> = props => (
    <div
        className={
            "deviceFrame orientation-button " +
            //  (props.selected ? "disabled " : "") +
            (props.landscape ? "landscape" : "portrait")
        }
        onClick={() => {
            //if (!props.selected) {
            props.onClick(props.landscape);
            //}
        }}
    >
        <div className={props.selected ? "selectedOrientation" : ""} />
    </div>
);
