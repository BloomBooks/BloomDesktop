import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, ILocalizationState, LocalizableElement } from "./l10n";

interface ComponentProps extends ILocalizationProps {
    clickEndpoint: string;
    onUpdateState: (string) => void;
    enabled: boolean;
}

interface ComponentState extends ILocalizationState {
}

// A button that takes a Bloom API endpoint to get() when clicked,
// a callback to call with whatever data Bloom API call returned,
// and a function to determine if this button should currently be enabled.
export default class BloomButton extends LocalizableElement<ComponentProps, ComponentState> {
    constructor(props) {
        super(props);
    }
    render() {
        return (
            <button
                onClick={() => axios.get("/bloom/api/" + this.props.clickEndpoint).then((response) => {
                    this.props.onUpdateState(response.data);
                })}
                disabled={!this.props.enabled}
            >
                {this.getLocalizedContent()}
            </button>
        );
    }
}
