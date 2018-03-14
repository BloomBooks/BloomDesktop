import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import ContentEditable from "./ContentEditable";

interface IColorChooserProps {
    imagePath: string;
    colorsVisibleByDefault: boolean;
    backColorSetting: string;
    colorPalette: string[];
    onColorChanged?: (string) => void;
}

interface IColorChooserState {
    colorsVisible: boolean;
}

// A reusable color chooser.
export class ColorChooser extends React.Component<IColorChooserProps, IColorChooserState> {
    constructor(props: IColorChooserProps) {
        super(props);
        this.state = { colorsVisible: props.colorsVisibleByDefault };
    }
    render() {
        return (
            <div className="cc-outer-wrapper" tabIndex={0} onClick={
                (event) => {
                    this.setState({ colorsVisible: !this.state.colorsVisible });
                }
            }>
                <div className="cc-image-wrapper">
                    <img className="cc-image"
                        // the api ignores the color parameter, but it
                        // causes this to re-request the img whenever the backcolor changes
                        src={this.props.imagePath + this.props.backColorSetting}></img>
                </div>
                <div className="cc-menu-arrow">
                    <div className="cc-pulldown-wrapper" style={{ visibility: (this.state.colorsVisible ? "visible" : "hidden") }}>
                        {this.props.colorPalette.map((color, i) =>
                            <div className="cc-color-option" key={i} style={{ backgroundColor: color }} data-color={color} onClick={
                                (event) => {
                                    const newColor = event.currentTarget.getAttribute("data-color");
                                    this.props.onColorChanged(newColor);
                                }}>
                            </div>)}
                        <div className="cc-hex-wrapper" onClick={(event) => event.stopPropagation()}>
                            <div className="cc-hex-leadin">#</div>
                            <div className="cc-hex-value">
                                <ContentEditable content={this.props.backColorSetting.substring(1)} onChange={(newContent => {
                                    this.props.onColorChanged("#" + newContent);
                                })} onEnterKeyPressed={() => this.setState({ colorsVisible: false })} />
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        );
    }
}
