import { css } from "@emotion/react";
import { kBloomBlue } from "../bloomMaterialUITheme";
import {
    ILocalizationProps,
    ILocalizationState,
    LocalizableElement
} from "./l10nComponents";
import * as MarkdownIt from "markdown-it";
import * as MarkdownItAttrs from "markdown-it-attrs";

// This component expects its content to be a single string (like all localizable elements) that
// contains Markdown. It will convert that into HTML and show it.
const markd = new MarkdownIt();
markd.use(MarkdownItAttrs, { allowedAttributes: ["id", "class"] });

export class Markdown extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return (
            <div
                css={css`
                    a {
                        color: ${kBloomBlue};
                    }
                `}
                className={this.getClassName()}
                onClick={() => {
                    if (this.props.onClick) {
                        this.props.onClick();
                    }
                }}
                dangerouslySetInnerHTML={{
                    __html: markd.render(this.state.translation || "")
                }}
            />
        );
    }
}
