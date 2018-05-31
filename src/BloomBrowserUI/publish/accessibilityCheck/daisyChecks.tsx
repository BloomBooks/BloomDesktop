import * as React from "react";
import * as ReactDOM from "react-dom";
import { IUILanguageAwareProps } from "../../react_components/l10n";

import "./daisyChecks.less";

export class DaisyChecks extends React.Component<IUILanguageAwareProps> {
    constructor(props) {
        super(props);
        this.state = {};
    }

    public render() {
        return <div>Daisy, daisy... </div>;
    }
}
