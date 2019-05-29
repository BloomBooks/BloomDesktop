import { CancelTokenStatic } from "axios";
import * as React from "react";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";
import { BloomApi } from "../utils/bloomApi";
import { getLocalization } from "./l10n";

// set the following boolean to highlight all translated strings to see if any are missing
const highlightTranslatedStrings: boolean = false;

export let channelName: string = ""; // ensure it's defined non-null
BloomApi.get("/common/channel", r => {
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
    l10nParam0?: string;
    l10nParam1?: string;
    onClick?: () => void; // not yet implemented by String subclass and maybe others outside this file
}

export interface ILocalizationState {
    translation?: string;
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
    private localizedText: string;

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

    // reverse-engineered from React/TSX
    // handles only one level of markup without any attributes.  Making this recursive
    // may well work, but YAGNI.
    public extractRawHtml(item: object): string {
        const type = item["type"];
        const props = item["props"];
        if (type != null && typeof type === "string" && props != null) {
            const children = props["children"];
            if (typeof children === "string") {
                return "<" + type + ">" + children + "</" + type + ">";
            }
        }
        return "[UNKNOWN DATA]";
    }

    public componentDidUpdate() {
        if (this.localizedText && this.props.l10nParam0) {
            let newText = this.localizedText.replace(
                "%0",
                this.props.l10nParam0!
            );
            if (this.props.l10nParam1) {
                newText = newText.replace("%1", this.props.l10nParam1!);
            }
            if (newText != this.state.translation) {
                this.setState({
                    translation: newText
                });
            }
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
        if (!english.startsWith("ERROR: must have exactly one child")) {
            getLocalization({
                english,
                l10nKey: this.props.l10nKey,
                l10nComment: this.props.l10nComment,
                l10nParam0: this.props.l10nParam0,
                l10nParam1: this.props.l10nParam1,
                callback: (localizedText, success) => {
                    this.localizedText = localizedText;
                    if (this.isComponentMounted) {
                        this.setState({
                            translation: localizedText,
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
        } else if (
            this.state &&
            this.state.translation &&
            this.state.lookupSuccessful
        ) {
            l10nClass = "translated";
            text = theOneLocalizationManager.processSimpleMarkdown(
                this.state.translation
            );
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

export class String extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return this.getLocalizedContent();
    }
}

export interface ILabelProps extends ILocalizationProps {}

export class Label extends LocalizableElement<ILabelProps, ILocalizationState> {
    public render() {
        return (
            <label
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
