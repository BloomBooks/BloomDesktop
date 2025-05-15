import { CancelTokenStatic } from "axios";
import * as React from "react";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";
import { get } from "../utils/bloomApi";
import { getLocalization } from "./l10n";

// set the following boolean to highlight all translated strings to see if any are missing
const highlightTranslatedStrings: boolean = false;

export let channelName: string = ""; // ensure it's defined non-null
get("/common/channel", r => {
    channelName = r.data;
    // Setting the class on the body element here to include the channel appears
    // to work for the places that this code affects.  Should we want to expand
    // marking untranslated strings visually in other places, then it would be
    // necessary to make this assignment in C# code.  Which unfortunately would
    // have to happen in at least half a dozen places.
    let channelClass = channelName.toLowerCase();
    if (channelClass.startsWith("developer/")) channelClass = "developer";
    if (document && document.body) {
        document.body.classList.add(channelClass);
        if (highlightTranslatedStrings) {
            document.body.classList.add("highlightTranslatedStrings");
        }
    }
});

// This would be used by a control that doesn't have any text of its own,
// but has children that need to be localized.
export interface IUILanguageAwareProps {
    currentUILanguage?: string;
    hidden?: boolean;
    className?: string;
}

export interface ILocalizationProps extends IUILanguageAwareProps {
    l10nKey: string;
    l10nComment?: string;
    l10nTipEnglishEnabled?: string;
    l10nTipEnglishDisabled?: string;
    alreadyLocalized?: boolean; // true if translated by C#-land
    l10nParams?: string[]; // Array of parameters to use in format strings
    l10nParam0?: string; // Legacy parameter 0 (for backward compatibility)
    l10nParam1?: string; // Legacy parameter 1 (for backward compatibility)
    onClick?: () => void; // not yet implemented by String subclass and maybe others outside this file
    id?: string; // not yet implented by all

    // Set to true if we don't want the yellow highlighting in the UI for now.
    // Typically this is used when the UI is still is such flux that we
    // don't want the strings in Crowdin yet.
    temporarilyDisableI18nWarning?: boolean;
}

export interface ILocalizationState {
    retrievedTranslation?: string; // what came back from getLocalization; may have {0}, %1 etc; may remain undefined if alreadyLocalized
    translation?: string; // The final thing we want to display, translated and with {0}, %1 etc replaced
    tipEnabledTranslation?: string;
    tipDisabledTranslation?: string;
    lookupSuccessful?: boolean;
}

// A base class for all elements that display text. It uses Bloom's localizationManager wrapper to get strings.
export class LocalizableElement<
    P extends ILocalizationProps,
    S extends ILocalizationState
> extends React.Component<P, ILocalizationState> {
    public readonly state: ILocalizationState = {};
    private localizationRequestCancelToken: CancelTokenStatic;
    private isComponentMounted: boolean;
    private tooltipKey: string;
    private disabledTooltipKey: string;

    private previousL10nKey: string = "";

    constructor(props: ILocalizationProps) {
        super(props as P);
        this.isComponentMounted = false; // This is an antipattern. See note on componentWillUnmount()
        this.tooltipKey = this.props.l10nKey + ".ToolTip";
        this.disabledTooltipKey = this.props.l10nKey + ".ToolTipWhenDisabled";
    }

    private getOriginalStringContent(): string {
        // Note the following *looks* better, but React complains that there is not exactly one child
        // even though React.Children.count returns 1.
        //      this.translated = React.Children.only(this.props.children).toString();
        const count = React.Children.count(this.props.children);
        const children = React.Children.toArray(this.props.children);
        if (count === 1 && typeof children[0] === "string") {
            return children[0]!.toString();
        }
        // Take a stab at handling multiple nodes (text/element) in the original TSX.  This isn't
        // too critical if you put the equivalent string in the xliff file, which isn't too bad for
        // <strong> or <em> represented by **...** or *...* (Markdown notation).
        let retval = "";
        for (let i = 0; i < count; ++i) {
            const item = children[i]!.valueOf();
            if (typeof item === "object") {
                const htmlString = this.extractRawHtml(item);
                retval = retval + htmlString;
            } else {
                retval = retval + item;
            }
        }
        return retval;
    }

    private attributeString(tagType: string, props: any): string {
        // warning: don't try to access props["href"] unless you're dealing with an anchor tag.
        return tagType === "a" ? ' href="' + props["href"] + '"' : "";
    }

    // reverse-engineered from React/TSX
    // handles only one level of markup without most attributes.  Making this recursive
    // may well work, but YAGNI.
    // We make one exception to include a href attribute, if the html is an anchor tag.
    public extractRawHtml(item: object): string {
        const type = item["type"];
        const props = item["props"];
        if (type != null && typeof type === "string" && props != null) {
            const children = props["children"];
            if (typeof children === "string") {
                return `<${type}${this.attributeString(
                    type,
                    props
                )}>${children}</${type}>`;
            }
        }
        return "[UNKNOWN DATA]";
    }

    public componentDidUpdate() {
        // Create a parameters array, prioritizing the l10nParams array if provided
        // Otherwise, fall back to the individual l10nParam0 and l10nParam1 properties
        const params = this.props.l10nParams || [
            this.props.l10nParam0,
            this.props.l10nParam1
        ];

        const newText = theOneLocalizationManager.simpleFormat(
            this.state.retrievedTranslation ?? this.getOriginalStringContent(),
            params
        );
        if (newText != this.state.translation) {
            this.setState({
                translation: newText
            });
        }
        if (this.props.l10nKey != this.previousL10nKey) {
            this.fetchTranslation();
        }
    }

    /*
    React Docs: "If you need to load data from a remote endpoint, componentDidMount is a good place to instantiate the network request.
    Setting state in this method will trigger a re-rendering."

    However, only doing the fetch here means that we don't re-fetch if the parent changes the string they want to show by changing
    the props and the English string. In one day-wasting example, the parent had two different <Label> </Label> elements, and
    switched between then, but react didn't understand that they were different (until we manually added a @key attribute). React
    was expecting that the component would react to the props changing; but it couldn't so long as we were only looking at
    them in componentDidMount, which is not called just because a render() of our parent changed our props.
    So now we look at props in componentDidUpdate() in addition. We cannot only fetch in componentDidUpdate because
    componentDidUpdate is not called for the initial render.
    */

    public componentDidMount() {
        this.fetchTranslation();
    }

    private fetchTranslation() {
        this.previousL10nKey = this.props.l10nKey;
        if (!this.props.l10nKey) {
            console.log("l10n component mounted with no key.");
            return;
        }
        this.isComponentMounted = true;
        if (this.props.alreadyLocalized) {
            return;
        }
        const english = this.getOriginalStringContent();
        // Prevent unnecessary translation lookup for image-only buttons. (BL-7204)
        const wantTranslation =
            !english && "hasText" in this.props
                ? this.props["hasText"]
                : !english.startsWith("ERROR: must have exactly one child");
        if (wantTranslation) {
            getLocalization({
                english,
                l10nKey: this.props.l10nKey,
                l10nComment: this.props.l10nComment,
                // do NOT pass these. Doing so is confusing, because this is called from componentDidMount,
                // and it's possible these params are retrieved by the caller from axios and are still null
                // when componentDidMount is called. l10nParams need to be handled by something that
                // will re-compute (like componentDidUpdate). OTOH, we generally don't need to re-call
                // getLocalization when they change...they are used in the output that it produces.
                // l10nParam0: this.props.l10nParam0,
                // l10nParam1: this.props.l10nParam1,
                temporarilyDisableI18nWarning: this.props
                    .temporarilyDisableI18nWarning,
                callback: (localizedText, success) => {
                    if (this.isComponentMounted) {
                        this.setState({
                            retrievedTranslation: localizedText,
                            lookupSuccessful: success
                        });
                    }
                }
            });
        }
        if (this.props.l10nTipEnglishEnabled) {
            theOneLocalizationManager
                .asyncGetText(
                    this.tooltipKey,
                    this.props.l10nTipEnglishEnabled!,
                    this.props.l10nComment
                )
                .done(result => {
                    if (this.isComponentMounted) {
                        this.setState({ tipEnabledTranslation: result });
                    }
                });
        }
        if (this.props.l10nTipEnglishDisabled) {
            theOneLocalizationManager
                .asyncGetText(
                    this.disabledTooltipKey,
                    this.props.l10nTipEnglishDisabled!,
                    this.props.l10nComment
                )
                .done(result => {
                    if (this.isComponentMounted) {
                        this.setState({ tipDisabledTranslation: result });
                    }
                });
        }
    }

    public componentWillUnmount() {
        //todo: we ought to have a way of cancelling this, using axios's CancelToken.
        // we can then get rid of the isMounted antipattern. But we would need to add
        // a parameter to pass that token to the theOneLocalizationManager.asyncGetText()
    }

    public getLocalizedContent(): JSX.Element {
        const parts = this.getLocalizedContentAndClass();
        const idxStart = parts.text.search(/<.*>/);
        if (idxStart >= 0) {
            const idxEnd1 = parts.text.indexOf("</", idxStart + 1);
            const idxEnd2 = parts.text.indexOf("/>", idxStart + 1);
            if (idxEnd1 > idxStart || idxEnd2 > idxStart) {
                return (
                    <span
                        className={parts.l10nClass}
                        dangerouslySetInnerHTML={{ __html: parts.text }}
                    />
                );
            }
        }
        return <span className={parts.l10nClass}>{parts.text}</span>;
    }

    protected getLocalizedContentAndClass(): {
        text: string;
        l10nClass: string;
    } {
        let l10nClass = "untranslated";
        let text = this.getOriginalStringContent();
        if (this.props.alreadyLocalized) {
            l10nClass = "assumedTranslated";
        } else if (this.state && this.state.translation) {
            if (this.state.lookupSuccessful) {
                l10nClass = "translated";
            } else if (this.props.temporarilyDisableI18nWarning) {
                l10nClass = "assumedTranslated";
            }
            text = theOneLocalizationManager.processSimpleMarkdown(
                this.state.translation
            );
        } else if (this.props.temporarilyDisableI18nWarning) {
            l10nClass = "assumedTranslated";
        }
        return { text: text, l10nClass: l10nClass };
    }

    // Should return the same text that getLocalizedContent would wrap in a span
    public getPlainLocalizedContent(): string {
        const parts = this.getLocalizedContentAndClass();
        return parts.text;
    }

    public getLocalizedTooltip(controlIsEnabled: boolean): string {
        return (
            (controlIsEnabled
                ? this.state.tipEnabledTranslation
                : this.state.tipDisabledTranslation
                ? this.state.tipDisabledTranslation
                : this.state.tipEnabledTranslation) || ""
        );
    }

    public getClassName(): string {
        return (
            (this.props.hidden ? "hidden " : "") + this.props.className
        ).trim();
    }
}

export class H1 extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return (
            <h1
                className={this.getClassName()}
                onClick={() => {
                    if (this.props.onClick) {
                        this.props.onClick();
                    }
                }}
            >
                {this.getLocalizedContent()}
            </h1>
        );
    }
}

export class H2 extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return (
            <h2
                className={this.getClassName()}
                onClick={() => {
                    if (this.props.onClick) {
                        this.props.onClick();
                    }
                }}
            >
                {this.getLocalizedContent()}
            </h2>
        );
    }
}

export class H3 extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return (
            <h3
                className={this.getClassName()}
                onClick={() => {
                    if (this.props.onClick) {
                        this.props.onClick();
                    }
                }}
            >
                {this.getLocalizedContent()}
            </h3>
        );
    }
}

export class P extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return (
            <p
                className={this.getClassName()}
                onClick={() => {
                    if (this.props.onClick) {
                        this.props.onClick();
                    }
                }}
            >
                {this.getLocalizedContent()}
            </p>
        );
    }
}

export class Div extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return (
            <div
                className={this.getClassName()}
                id={this.props.id}
                onClick={() => {
                    if (this.props.onClick) {
                        this.props.onClick();
                    }
                }}
            >
                {this.getLocalizedContent()}
            </div>
        );
    }
}

export class LocalizedString extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return this.getLocalizedContent();
    }
}

interface ILabelProps {
    htmlFor?: string;
}

export class Label extends LocalizableElement<
    ILocalizationProps & ILabelProps,
    ILocalizationState
> {
    public render() {
        return (
            <label
                htmlFor={this.props.htmlFor}
                className={this.getClassName()}
                onClick={() => {
                    if (this.props.onClick) {
                        this.props.onClick();
                    }
                }}
            >
                {this.getLocalizedContent()}
            </label>
        );
    }
}

export class Span extends LocalizableElement<
    ILocalizationProps & React.HTMLAttributes<HTMLElement>,
    ILocalizationState
> {
    public render() {
        const { onClick, l10nKey, ...restOfProps } = this.props;
        return (
            <span
                {...restOfProps}
                className={this.getClassName()}
                onClick={() => {
                    if (this.props.onClick) {
                        this.props.onClick();
                    }
                }}
            >
                {this.getLocalizedContent()}
            </span>
        );
    }
}
