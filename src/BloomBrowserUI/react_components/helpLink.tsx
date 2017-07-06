import * as React from "react";
import { ILocalizationProps, LocalizableElement } from "./l10n";


interface ComponentProps extends ILocalizationProps {
    helpId: string;
}

// just an html anchor that knows how to localize and how turn a Bloom help id into a url
export default class HelpLink extends LocalizableElement<ComponentProps, {}> {
    constructor(props) {
        super(props);
    }
    render() {
        return (
            <a href={"/bloom/api/help/" + this.props.helpId}>
                {this.getLocalizedContent()}
            </a>
        );
    }
}
