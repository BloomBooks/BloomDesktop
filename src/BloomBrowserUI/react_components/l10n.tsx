import axios from "axios";
import { CancelTokenStatic } from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";

// This would be used by a control that doesn't have any text of its own,
// but has children that need to be localized.
export interface IUILanguageAwareProps {
    currentUILanguage?: string;
    //l10nVerbose?: boolean
}

export interface ILocalizationProps extends IUILanguageAwareProps {
    l10nkey: string;
    l10ncomment?: string;
}

export interface ILocalizationState {
    translation?: string;
}

// A base class for all elements that display text. It uses Bloom's localizationManager wrapper to get strings.
export class LocalizableElement<P extends ILocalizationProps, S extends ILocalizationState> extends React.Component<P, ILocalizationState> {
    localizationRequestCancelToken: CancelTokenStatic;
    isComponentMounted: boolean;
    constructor(props: ILocalizationProps) {
        super(props as P);
        this.isComponentMounted = false; // This is an antipattern. See note on componentWillUnmount()
        this.state = {};
    }
    private getOriginalEnglishStringContent(): string {
        // Note the following *looks* better, but React complains that there is not exactly one child
        // even though React.Children.count returns 1.
        //      this.translated = React.Children.only(this.props.children).toString();
        if (React.Children.count(this.props.children) === 1) {
            return React.Children.toArray(this.props.children)[0].toString();
        } else {
            return "ERROR: must have exactly one child (a text string). Cannot yet handle any elements like spans.";
        }
    }

    // React Docs: "If you need to load data from a remote endpoint, this is a good place to instantiate the network request.
    // Setting state in this method will trigger a re-rendering."
    public componentDidMount() {
        let self = this;
        this.isComponentMounted = true;
        theOneLocalizationManager.asyncGetText(this.props.l10nkey, this.getOriginalEnglishStringContent())
            .done(function (result) {
                // TODO: This isMounted approach is an official antipattern, to swallow exception if the result comes back
                // after this component is no longer visible. See note on componentWillUnmount()
                if (self.isComponentMounted) {
                    self.setState({ translation: result });
                }
            });
    }

    public componentWillUnmount() {
        //todo: we ought to have a way of cancelling this, using axios's CancelToken.
        // we can then get rid of the isMounted antipattern. But we would need to add
        // a parameter to pass that token to the theOneLocalizationManager.asyncGetText()
    }

    public getLocalizedContent(): JSX.Element {
        // if (l10nVerbose) { // enhance... I was playing with a "verbose" feature
        //     return <span>{(this.state as any) + "[l10nkey=" + this.props.l10nkey + " uilang=" +
        //            this.props.currentUILanguage + "]"} </span>;
        // } else {
        if (this.state.translation !== undefined) {
            return <span> {this.state.translation} </span>;
        } else {
            return <span style={{ color: "grey" }}> {this.getOriginalEnglishStringContent()} </span>;
        }
        //        }
    }
}

export class H1 extends LocalizableElement<ILocalizationProps, ILocalizationState> {
    render() {
        return (
            <h1>
                {this.getLocalizedContent()}
            </h1>
        );
    }
}

export class H2 extends LocalizableElement<ILocalizationProps, ILocalizationState> {
    render() {
        return (
            <h2>
                {this.getLocalizedContent()}
            </h2>
        );
    }
}

export class H3 extends LocalizableElement<ILocalizationProps, ILocalizationState> {
    render() {
        return (
            <h3>
                {this.getLocalizedContent()}
            </h3>
        );
    }
}

