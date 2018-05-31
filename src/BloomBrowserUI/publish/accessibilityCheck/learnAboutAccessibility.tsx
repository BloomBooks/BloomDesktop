import * as React from "react";
import * as ReactDOM from "react-dom";
import Link from "../../react_components/link";
import HelpLink from "../../react_components/helpLink";
import HtmlHelpLink from "../../react_components/htmlHelpLink";
import { H1, IUILanguageAwareProps } from "../../react_components/l10n";

import "./learnAboutAccessibility.less";

export class LearnAboutAccessibility extends React.Component<
    IUILanguageAwareProps
> {
    public render() {
        return <div>Happy learning!</div>;
    }
}
