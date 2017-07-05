import axios = require("axios");
import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, LocalizableElement } from "./l10n";

interface ComponentProps extends ILocalizationProps {
    clickEndpoint: string;
    onUpdateState: (string) => void;
    enabled: boolean;
}

interface ComponentState {
}

export default class BloomButton extends LocalizableElement<ComponentProps, ComponentState> {
    constructor(props) {
        super(props);
        let self = this;
        this.state = {};
    }
    render() {
        return (
            <button
                onClick={() => axios.get<string>("/bloom/api/" + this.props.clickEndpoint).then((response) => {
                    this.props.onUpdateState(response.data);
                })}
                disabled={!this.props.enabled}
            >
                {this.getLocalizedContent()}
            </button>
        );
    }
}
