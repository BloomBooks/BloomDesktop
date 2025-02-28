import * as React from "react";
import {
    ILocalizationProps,
    ILocalizationState,
    LocalizableElement
} from "./l10nComponents";
import * as MarkdownIt from "markdown-it";

// This component expects its content to be a single string (like all localizable elements) that
// contains Markdown. It will convert that into HTML and show it.
export class Markdown extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return (
            <div
                className={this.getClassName()}
                onClick={() => {
                    if (this.props.onClick) {
                        this.props.onClick();
                    }
                }}
                dangerouslySetInnerHTML={{
                    __html: new MarkdownIt().render(
                        this.state.translation || ""
                    )
                }}
            />
        );
    }
}
