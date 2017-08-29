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
            // we always provide the english path, but this api will return the best translation it finds
            <a target="_blank" href={"/api/externalLink/help/" + this.props.fileid + "-en.htm"}>
                {this.getLocalizedContent()}
            </a>
        );
    }
}
