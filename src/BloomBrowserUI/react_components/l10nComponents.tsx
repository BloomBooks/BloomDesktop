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
    // Remember to update localizationPropsKeys when modifying these keys

    l10nKey: string;
    l10nComment?: string;
    l10nTipEnglishEnabled?: string;
    l10nTipEnglishDisabled?: string;
    alreadyLocalized?: boolean; // true if translated by C#-land
    l10nParam0?: string;
    l10nParam1?: string;
    onClick?: () => void; // not yet implemented by String subclass and maybe others outside this file
}

// This object contains is an exemplar object of the ILocalizationProps interface
// It needs to specify each key, including the optional ones (including the optional ones of any parent interface that ILocalizationProps extends).
const localizationPropsKeys: ILocalizationProps = {
    l10nKey: "",
    l10nComment: undefined,
    l10nTipEnglishEnabled: undefined,
    l10nTipEnglishDisabled: undefined,
    alreadyLocalized: undefined,
    l10nParam0: undefined,
    l10nParam1: undefined,
    // onClick: undefined,  // Purposefully excluded because it's a duplicate of an HTML attribute
    currentUILanguage: undefined,
    hidden: undefined,
    className: undefined
};

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

    // Looks at this.props, and returns a copy without any of the keys in ILocalizationProps
    public getStandardHtmlProps(): React.HTMLAttributes<HTMLElement> {
        return this.removeCustomProps(localizationPropsKeys);
    }

    // Looks at this.props and removes any props belonging to a custom type
    // Returns a new object which passes through only props belonging to the standard HTML
    //
    // customTypeExemplar - An object of the custom type.
    //   It should include every field in the custom type. (null/undefined is ok)
    //   Take special care to make sure it includes every optional field in the type and any types it derives from
    //     (It's harder to mess up the required fields because the compiler will detect those. But it'll let missing optional fields slide.)
    //
    // Note: if an prop key is in both the ${customTypeExemplar} and is also a standard HTML prop,
    //       it will be treated the same as any other custom prop. (That is, removed)
    //       If desired, you may wish for ${customTypeExemplar} to exclude fields which are included in the custom type but also included in Standard HTML attributes
    //       Then it would return ALL of the standard ones.
    public removeCustomProps(
        customTypeExemplar: object
    ): React.HTMLAttributes<HTMLElement> {
        // We may often have properties that are a union of types.
        // Here is a common pattern:
        //     ILocalizationProps & React.HTMLAttributes<HTMLElement>
        // We would like to be able to separate out which subset belong to ILocalizationProps and which subset belongs to HTMLAttributes.
        // However, since Javascript doesn't really have much typing at runtime (the type system in TypeScript is compile time),
        // there's not an easy way to just figure out via reflection which fields are in HTMLAttributes.
        // But if we have an exemplar object of one of the types (an object whose fields are exactly equal to the fields of a class),
        // now it becomes a tractable problem to figure out which fields belong to one or the other.
        // (At runtime we can examine the fields of an OBJECT, but not the fields of a TYPE/CLASS/INTERFACE.)
        //
        // Ideally, it would be better to have an exemplar object of the HTMLAttributes...
        // But, 1) Probably lengthier to list
        //      2) Not actually so straightforward to define what are all the attributes.  It can vary depending on whether it's a SPAN, INPUT, etc.
        // It's more tractable to be given the custom props, and remove these and be left with the standard props.
        const htmlProps: React.HTMLAttributes<HTMLElement> = {};

        Object.keys(this.props).forEach(key => {
            if (!(key in customTypeExemplar)) {
                htmlProps[key] = this.props[key];
            }
        });

        return htmlProps;
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
                {...this.getStandardHtmlProps()}
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
                {...this.getStandardHtmlProps()}
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
                {...this.getStandardHtmlProps()}
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
            <p className={this.getClassName()} {...this.getStandardHtmlProps()}>
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
                {...this.getStandardHtmlProps()}
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

export interface ILabelProps extends ILocalizationProps {}

export class Label extends LocalizableElement<ILabelProps, ILocalizationState> {
    public getStandardHtmlProps() {
        // Theoretically, we should override this function because using ILabelProps instead of ILocalizationPros,
        // but currently there is no difference.
        return super.getStandardHtmlProps();
    }

    public render() {
        return (
            <label
                className={this.getClassName()}
                {...this.getStandardHtmlProps()}
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
        return (
            <span
                className={this.getClassName()}
                {...this.getStandardHtmlProps()}
            >
                {this.getLocalizedContent()}
            </span>
        );
    }
}
