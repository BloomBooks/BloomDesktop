import * as React from "react";
import * as ReactDOM from "react-dom";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

// This would be used by a control that doesn't have any text of its own,
// but has children that need to be localized.
export interface IUILanguageAwareProps {
    currentUILanguage?: string;
    // a mode that makes it easier to ensure all the l10 is wired up yet.
    verbosel10n?: boolean;
}

export interface ILocalizationProps extends IUILanguageAwareProps {
    l10nkey: string;
    l10ncomment?: string;
}

// a base class for all elements that display text.
export class LocalizableElement<P extends ILocalizationProps, S> extends React.Component<P, S> {
    translated: string;
    constructor(props: ILocalizationProps) {
        super(props as P);
        let xx = this;
        //        props.verbosel10n = true;
    }

    /*    public componentDidMount() {
            let inner = ReactDOM.findDOMNode(this).innerHTML;
            this.translated = inner;
            // do some translation asynchronously
            theOneLocalizationManager.asyncGetText(this.props.l10nkey, inner)
                .done(function (result) {
                    this.translated = result; // todo: probably needs to be state to cause redraw
                });
        }
    */
    public getLocalizedContent(): string {
        if (this.props.verbosel10n) {
            return "${this.translated} [l10nkey=${this.props.l10nkey}, uilang=${this.props.currentUILanguage]}";
        } else {
            return "blahblah";// this.translated;
        }
    }
}

export class H1 extends LocalizableElement<ILocalizationProps, {}> {
    constructor(props) {
        super(props);
        let self = this;
        this.state = {};
    }
    render() {
        return (
            <h1>
                {/*this.getLocalizedContent()*/}
                {this.props.children}
            </h1>
        );
    }
}
export class H2 extends React.Component<ILocalizationProps, {}> {
    render() {
        return (
            <h2>
                {this.props.children}
            </h2>
        );
    }
}
export class H3 extends LocalizableElement<ILocalizationProps, {}> {
    render() {
        return (
            <h3>
                {this.getLocalizedContent()}
            </h3>
        );
    }
}

