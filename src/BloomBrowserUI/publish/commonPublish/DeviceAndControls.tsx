import * as React from "react";
import "./DeviceFrame.less";
import { useState, useEffect } from "react";
import { Div } from "../../react_components/l10nComponents";

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
    onRefresh?: () => void;
}> = props => {
    const [landscape, setLandscape] = useState(props.defaultLandscape);
    useEffect(() => {
        setLandscape(props.defaultLandscape);
    }, [props]);

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
                    <div className="refresh-icon" />
                    <Div className="refresh" l10nKey="Common.Refresh">
                        Refresh
                    </Div>
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
