import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, ILocalizationState, LocalizableElement } from "./l10n";

interface ComponentProps extends ILocalizationProps {
    clickEndpoint: string;
    enabled: boolean;
}

interface ComponentState extends ILocalizationState {
}

// A button that takes a Bloom API endpoint to post() when clicked
// and a bool to determine if this button should currently be enabled.
export default class BloomButton extends LocalizableElement<ComponentProps, ComponentState> {
    constructor(props) {
        super(props);
    }
    render() {
        return (
            <button className={this.props.hidden ? "hidden" : ""}
                onClick={() => axios.post("/bloom/api/" + this.props.clickEndpoint)}
                disabled={!this.props.enabled}
            >
                {this.getLocalizedContent()}
            </button>
        );
    }
}
