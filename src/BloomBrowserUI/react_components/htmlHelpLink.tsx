import * as React from "react";
import { ILocalizationProps, LocalizableElement } from "./l10nComponents";
import Link from "@mui/material/Link";
import { getString } from "../utils/bloomApi";

interface IHtmlHelpLinkProps extends ILocalizationProps {
    // the root of the name, without the language suffix or extension
    fileid: string;
}

// an html anchor that has an l10n-aware label and also works with the bloom backend to
// give us the localized version of the target file, if it exists
export default class HtmlHelpLink extends LocalizableElement<
    IHtmlHelpLinkProps,
    {}
> {
    private target = `externalLink?path=help/${this.props.fileid}-en.htm`;

    public render() {
        return (
            // We always provide the english path, but this api will return the best translation it finds
            // N.B. Putting the api call in as the 'href' prop results in the browser jumping (on the
            // initial render) to the bare api string and THEN also jumping to the correct
            // "output/browser/help/{filename}" file. In fact, we can't even HAVE the 'href' prop to get
            // the correct behavior.
            <Link
                target="_blank"
                onClick={() =>
                    getString(this.target, dummy => {
                        // Do nothing
                    })
                }
                underline="hover"
            >
                {this.getLocalizedContent()}
            </Link>
        );
    }
}
