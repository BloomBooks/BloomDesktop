import * as React from "react";
import * as MUI from "@material-ui/core";

import { ILocalizationProps, LocalizableElement } from "./l10nComponents";

interface ILinkProps extends ILocalizationProps {
    id?: string;
    href?: string;
    onClick?: any; // overrides following any href.
}

// A link element that is localizable.
export class Link extends LocalizableElement<ILinkProps, {}> {
    public render() {
        // prettier-ignore
        return (<MUI.Link
                className={this.props.className}
                id={"" + this.props.id}
                // href must be defined in order to maintain normal link UI
                // I tried to do like the 'id' attribute above, but it caused an error.
                href={this.props.href ? this.props.href : ""}
                onClick={e => {
                    if (this.props.onClick) {
                        // If we have an onClick we don't expect to also have an href.
                        // In which case the code above will provide an empty href.
                        // The default behaviour will navigate to the top of the page,
                        // causing the whole react page to reload. So prevent that default.
                        e.preventDefault();
                        this.props.onClick();
                    }
                }}
            >{this.getLocalizedContent()}</MUI.Link>);
    }
}

export default Link;
