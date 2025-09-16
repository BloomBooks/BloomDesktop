import { css } from "@emotion/react";
import { kBloomBlue } from "../bloomMaterialUITheme";
import {
    ILocalizationProps,
    ILocalizationState,
    LocalizableElement,
} from "./l10nComponents";
import * as MarkdownItNS from "markdown-it";
import * as MarkdownItAttrsNS from "markdown-it-attrs";
// Vite provides ESM default at runtime; TS types need namespace import without esModuleInterop.
// Cast to any to access the runtime default constructor and plugin.
// Vite runs ESM, while our typings are CommonJS-style. Safely access default.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const MarkdownIt =
    ((MarkdownItNS as unknown) as { default: any }).default ||
    ((MarkdownItNS as unknown) as unknown);
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const markdownItAttrs =
    ((MarkdownItAttrsNS as unknown) as { default: any }).default ||
    ((MarkdownItAttrsNS as unknown) as unknown);

// This component expects its content to be a single string (like all localizable elements) that
// contains Markdown. It will convert that into HTML and show it.
const markd = new MarkdownIt();
markd.use(markdownItAttrs, { allowedAttributes: ["id", "class"] });

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
                    __html: markd.render(this.state.translation || ""),
                }}
            />
        );
    }
}
