import * as React from "react";
import * as ReactDOM from "react-dom";
import { IUILanguageAwareProps } from "../../react_components/l10n";

import "./accessibilityChecklist.less";

export class AccessibilityChecklist extends React.Component<
    IUILanguageAwareProps
> {
    constructor(props) {
        super(props);
        this.state = {};
    }

    public render() {
        return (
            <div>This will be the AccessibilityChecklist when it grows up.</div>
        );
    }
}
