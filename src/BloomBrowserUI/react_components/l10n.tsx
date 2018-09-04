import { CancelTokenStatic } from "axios";
import * as React from "react";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";

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
}

export interface ILocalizationState {
    translation?: string;
    tipEnabledTranslation?: string;
    tipDisabledTranslation?: string;
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

    // set the following boolean to turn all translated strings green
    private turnTranslatedGreen: boolean = false;

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
        if (React.Children.count(this.props.children) === 1) {
            return React.Children.toArray(this.props.children)[0].toString();
        } else {
            return "ERROR: must have exactly one child (a text string). Cannot yet handle any elements like spans.";
        }
    }

    public componentDidUpdate() {
        if (this.localizedText && this.props.l10nParam0) {
            var newText = this.localizedText.replace(
                "%0",
                this.props.l10nParam0
            );
            if (this.props.l10nParam1) {
                newText = newText.replace("%1", this.props.l10nParam1);
            }
            if (newText != this.state.translation) {
                this.setState({
                    translation: newText
                });
            }
        }
    }

    // React Docs: "If you need to load data from a remote endpoint, this is a good place to instantiate the network request.
    // Setting state in this method will trigger a re-rendering."
    public componentDidMount() {
        if (!this.props.l10nKey) {
            console.log("l10n component mounted with no key.");
            return;
        }
        this.isComponentMounted = true;
        if (this.props.alreadyLocalized) {
            return;
        }
        var english = this.getOriginalStringContent();
        if (!english.startsWith("ERROR: must have exactly one child")) {
            theOneLocalizationManager
                .asyncGetText(
                    this.props.l10nKey,
                    english,
                    this.props.l10nComment
                )
                .done(result => {
                    // TODO: This isMounted approach is an official antipattern, to swallow exception if the result comes back
                    // after this component is no longer visible. See note on componentWillUnmount()
                    if (this.props.l10nParam0) {
                        result = result.replace("%0", this.props.l10nParam0);
                        if (this.props.l10nParam1) {
                            result = result.replace(
                                "%1",
                                this.props.l10nParam1
                            );
                        }
                    }
                    this.localizedText = result;
                    if (this.isComponentMounted) {
                        this.setState({ translation: result });
                    }
                });
        }
        if (this.props.l10nTipEnglishEnabled) {
            theOneLocalizationManager
                .asyncGetText(
                    this.tooltipKey,
                    this.props.l10nTipEnglishEnabled,
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
                    this.props.l10nTipEnglishDisabled,
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
        if (this.props.alreadyLocalized) {
            if (this.turnTranslatedGreen) {
                // We'll use lightgreen to mean assumed translated by C#-land
                return (
                    <span style={{ color: "lightgreen" }}>
                        {" "}
                        {this.getOriginalStringContent()}{" "}
                    </span>
                );
            }
            return <span> {this.getOriginalStringContent()} </span>;
        }
        if (this.state && this.state.translation) {
            if (this.turnTranslatedGreen) {
                return (
                    <span style={{ color: "green" }}>
                        {" "}
                        {this.state.translation}{" "}
                    </span>
                );
            } else {
                return <span> {this.state.translation} </span>;
            }
        } else {
            return (
                <span style={{ color: "grey" }}>
                    {" "}
                    {this.getOriginalStringContent()}{" "}
                </span>
            );
        }
    }

    public getLocalizedTooltip(controlIsEnabled: boolean): string {
        return controlIsEnabled
            ? this.state.tipEnabledTranslation
            : this.state.tipDisabledTranslation
                ? this.state.tipDisabledTranslation
                : this.state.tipEnabledTranslation;
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
            <h1 className={this.getClassName()}>
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
            <h2 className={this.getClassName()}>
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
            <h3 className={this.getClassName()}>
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
            <p className={this.getClassName()}>{this.getLocalizedContent()}</p>
        );
    }
}

export class Div extends LocalizableElement<
    ILocalizationProps,
    ILocalizationState
> {
    public render() {
        return (
            <div className={this.getClassName()}>
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

export interface ILabelProps extends ILocalizationProps {
    onClick?: () => void; // enhance: possibly promote to LocalizableElement?
}

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
