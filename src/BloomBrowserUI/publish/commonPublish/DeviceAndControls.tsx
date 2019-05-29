import * as React from "react";
import "./DeviceFrame.less";
import { Button } from "@material-ui/core";
import { useState, useEffect } from "react";

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
}> = props => {
    const [landscape, setLandscape] = useState(props.defaultLandscape);
    useEffect(() => {
        setLandscape(props.defaultLandscape);
    }, [props]);

    console.log("^^^^^^^^landscape " + landscape.toString());
    return (
        <div className="deviceAndControls">
            <div
                className={
                    "deviceFrame fullSize " +
                    (landscape ? "landscape" : "portrait")
                }
            >
                <iframe title="book preview" src={props.url} />
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
