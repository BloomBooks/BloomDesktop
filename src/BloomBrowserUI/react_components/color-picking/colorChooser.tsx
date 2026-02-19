import * as React from "react";
import { useState } from "react";
import ContentEditable from "../ContentEditable";
import "./colorChooser.less";
import { CoverBackgroundPalette } from "./bloomPalette";

interface IColorChooserProps {
    imagePath?: string;
    initiallyVisible?: boolean;
    color: string;
    onColorChanged?: (color: string) => void;
    menuLeft?: boolean;
    disabled?: boolean;
}

// A reusable color chooser.
export const ColorChooser: React.FunctionComponent<IColorChooserProps> = (
    props,
) => {
    const [chooserVisible, setChooserVisible] = useState(
        !!props.initiallyVisible,
    );

    return (
        <div
            className="cc-outer-wrapper"
            tabIndex={0}
            onClick={(event) => {
                if (!props.disabled) {
                    setChooserVisible(!chooserVisible);
                }
            }}
            onBlur={(event) => {
                if (!props.disabled) {
                    setChooserVisible(false);
                }
            }}
            onKeyDown={(event) => {
                if (event.code === "Escape" && !props.disabled) {
                    setChooserVisible(false);
                }
            }}
            {...props} // allow styling from parent
        >
            {props.imagePath && (
                <div className="cc-image-wrapper">
                    <img
                        className="cc-image"
                        // the api ignores the color parameter, but it
                        // causes this to re-request the img whenever the backcolor changes
                        src={props.imagePath + props.color}
                    />
                </div>
            )}
            <div
                className={
                    "cc-menu-arrow" +
                    (props.menuLeft ? " cc-pulldown-left" : "") +
                    (props.disabled ? " disabled" : "")
                }
            >
                <div
                    className="cc-pulldown-wrapper"
                    style={{
                        visibility: chooserVisible ? "visible" : "hidden",
                    }}
                >
                    {CoverBackgroundPalette.map((color, i) => (
                        <div
                            className="cc-color-option"
                            key={i}
                            style={{ backgroundColor: color }}
                            data-color={color}
                            onClick={(event) => {
                                const newColor =
                                    event.currentTarget.getAttribute(
                                        "data-color",
                                    );
                                if (props.onColorChanged && newColor) {
                                    props.onColorChanged(newColor);
                                }
                            }}
                        />
                    ))}
                    <div
                        className="cc-hex-wrapper"
                        onClick={(event) => event.stopPropagation()}
                    >
                        <div className="cc-hex-leadin">#</div>
                        <div className="cc-hex-value">
                            <ContentEditable
                                content={props.color.substring(1)}
                                onChange={(newContent) => {
                                    if (props.onColorChanged) {
                                        if (!newContent)
                                            props.onColorChanged("#FFFFFF");
                                        else
                                            props.onColorChanged(
                                                "#" + newContent.trim(),
                                            );
                                    }
                                }}
                                onEnterKeyPressed={() =>
                                    setChooserVisible(false)
                                }
                            />
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};
