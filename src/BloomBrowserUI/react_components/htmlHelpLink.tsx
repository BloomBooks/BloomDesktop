import * as React from "react";
import { ILocalizationProps, LocalizableElement } from "./l10n";


interface ComponentProps extends ILocalizationProps {
    // the root of the name, without the language suffix or extension
    fileid: string;
}

// an html anchor that has an l10n-aware label and also works with the bloom backend to
// give us the localized version of the target file, if it exists
export default class HtmlHelpLink extends LocalizableElement<ComponentProps, {}> {
    render() {
        return (
            <a target="_blank" href={"/bloom/htmlhelp/" + this.props.fileid}>
                {this.getLocalizedContent()}
            </a>
        );
    }
}
