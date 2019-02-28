import * as React from "react";
import * as ReactDOM from "react-dom";
import ContentEditable from "./ContentEditable";
import "./colorChooser.less";

interface IColorChooserProps {
    imagePath: string;
    colorsVisibleByDefault?: boolean;
    backColorSetting: string;
    colorPalette?: string[];
    onColorChanged?: (string) => void;
    menuLeft?: boolean;
}

interface IColorChooserState {
    colorsVisible: boolean;
}

let useColorPalette: string[] = [
    "#E48C84",
    "#B0DEE4",
    "#98D0B9",
    "#C2A6BF",
    "#FFFFA4",
    "#FEBF00",
    "#7BDCB5",
    "#B2CC7D",
    "#F8B576",
    "#D29FEF",
    "#ABB8C3",
    "#C1EF93",
    "#FFD4D4",
    "#FFAAD4"
];
let useVisibility: boolean = false;

// A reusable color chooser.
export class ColorChooser extends React.Component<
    IColorChooserProps,
    IColorChooserState
> {
    public readonly state: IColorChooserState = {
        colorsVisible: useVisibility
    };

    constructor(props: IColorChooserProps) {
        super(props);
        if (this.props.colorPalette) {
            useColorPalette = this.props.colorPalette;
        }
        if (this.props.colorsVisibleByDefault) {
            useVisibility = this.props.colorsVisibleByDefault;
        }
        this.setState({ colorsVisible: useVisibility });
    }
    public render() {
        return (
            <div
                className="cc-outer-wrapper"
                tabIndex={0}
                onClick={event => {
                    this.setState({ colorsVisible: !this.state.colorsVisible });
                }}
            >
                <div className="cc-image-wrapper">
                    <img
                        className="cc-image"
                        // the api ignores the color parameter, but it
                        // causes this to re-request the img whenever the backcolor changes
                        src={this.props.imagePath + this.props.backColorSetting}
                    />
                </div>
                <div
                    className={
                        "cc-menu-arrow" +
                        (this.props.menuLeft ? " cc-pulldown-left" : "")
                    }
                >
                    <div
                        className="cc-pulldown-wrapper"
                        style={{
                            visibility: this.state.colorsVisible
                                ? "visible"
                                : "hidden"
                        }}
                    >
                        {useColorPalette.map((color, i) => (
                            <div
                                className="cc-color-option"
                                key={i}
                                style={{ backgroundColor: color }}
                                data-color={color}
                                onClick={event => {
                                    const newColor = event.currentTarget.getAttribute(
                                        "data-color"
                                    );
                                    if (this.props.onColorChanged) {
                                        this.props.onColorChanged(newColor);
                                    }
                                }}
                            />
                        ))}
                        <div
                            className="cc-hex-wrapper"
                            onClick={event => event.stopPropagation()}
                        >
                            <div className="cc-hex-leadin">#</div>
                            <div className="cc-hex-value">
                                <ContentEditable
                                    content={this.props.backColorSetting.substring(
                                        1
                                    )}
                                    onChange={newContent => {
                                        if (this.props.onColorChanged) {
                                            this.props.onColorChanged(
                                                "#" + newContent
                                            );
                                        }
                                    }}
                                    onEnterKeyPressed={() =>
                                        this.setState({ colorsVisible: false })
                                    }
                                />
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        );
    }
}
