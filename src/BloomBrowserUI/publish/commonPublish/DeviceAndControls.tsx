/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import "./DeviceFrame.less";
import { useState, useEffect } from "react";
import { useDrawAttention } from "../../react_components/UseDrawAttention";
import BloomButton from "../../react_components/bloomButton";
/*
  Example usage:
  <DeviceFrame defaultLandscape={true}>
    <iframe..../>
  </DeviceFrame>
*/

const commonDeviceFrameCss = `
background: rgb(241, 241, 241);
// Our width & height are sizing the screen (the glass). All the bezel and stuff doesn't count.
box-sizing: content-box;
border-width: 10px;
border-style: solid;
border-color: #636363;
border-top-width: 20px;
border-bottom-width: 20px;
border-radius: 15px;
position: relative;
flex-direction: column;
flex-shrink: 0;
display: flex !important;
transition: all 200ms;
`;

export const DeviceAndControls: React.FunctionComponent<{
    defaultLandscape: boolean;
    canRotate: boolean;
    url: string;
    iframeClass?: string;
    showPreviewButton?: boolean;
    highlightPreviewButton?: boolean;
    onPreviewButtonClicked?: () => void;
    // hide theactual preview (typically if we're waiting for some data, especially orientation)
    hidePreview?: boolean;
}> = props => {
    const [landscape, setLandscape] = useState(props.defaultLandscape);

    useEffect(() => {
        setLandscape(props.defaultLandscape);
    }, [props]);

    const attentionClass = useDrawAttention(
        props.highlightPreviewButton ? 1 : 0,
        () => {
            return !props.highlightPreviewButton;
        }
    );

    return (
        <div
            className="deviceAndControls"
            css={css`
                min-width: 400px;
                display: flex;
                flex-direction: column;
            `}
        >
            {props.showPreviewButton && (
                <div
                    css={css`
                        display: flex;
                        flex-direction: row;
                        padding: 15px 0;
                        // It seems counter-intuitive to put a height on this row, but if we don't, the
                        // "large" orientation buttons that are scaled down, take up the whole rest of the
                        // screen, throwing off the layout of the preview below.
                        height: ${props.canRotate ? "70px" : "40px"};
                    `}
                    className={
                        "preview-controls-row" +
                        (landscape ? " landscape" : "") +
                        (props.canRotate ? " with-orientation-buttons" : "")
                    }
                >
                    <BloomButton
                        aria-label="refresh preview"
                        className={"refresh-icon " + attentionClass}
                        css={css`
                            min-width: 120px;
                            height: 40px;
                        `}
                        l10nKey="Common.Preview"
                        enabled={true}
                        onClick={() => props.onPreviewButtonClicked?.()}
                        hasText={true}
                        size="large"
                    >
                        Preview
                    </BloomButton>
                    {props.canRotate && (
                        <React.Fragment>
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
                        </React.Fragment>
                    )}
                </div>
            )}
            <div
                // Need a small tweak here if we have orientation buttons above us.
                css={css`
                    ${commonDeviceFrameCss}
                    ${props.canRotate ? "margin-top: -15px;" : ""}
                    ${props.hidePreview ? "visibility: hidden;" : ""}
                `}
                className={
                    "deviceFrame fullSize " +
                    (landscape ? "landscape" : "portrait")
                }
            >
                <iframe
                    id="preview-iframe"
                    css={css`
                        background-color: black;
                        border: none;
                        flex-shrink: 0; // without this, the height doesn't grow
                        transform-origin: top left;
                    `}
                    title="book preview"
                    src={props.url}
                    className={props.iframeClass}
                />
            </div>
        </div>
    );
};

const OrientationButton: React.FunctionComponent<{
    landscape: boolean;
    selected: boolean;
    onClick: (landscape: boolean) => void;
}> = props => (
    <div
        css={css`
            ${commonDeviceFrameCss}
            margin-top: -10px;
            margin-left: -55px;
        `}
        className={
            "deviceFrame orientation-button " +
            (props.landscape ? "landscape" : "portrait")
        }
        onClick={() => {
            props.onClick(props.landscape);
        }}
    >
        <div className={props.selected ? "selectedOrientation" : ""} />
    </div>
);
