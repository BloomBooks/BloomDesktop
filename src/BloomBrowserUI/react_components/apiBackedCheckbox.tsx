import * as React from "react";
import { ILocalizationProps } from "./l10n";
import { BloomApi } from "../utils/bloomApi";
import { Checkbox } from "./checkbox";

// Use this component when you have a one-to-one correspondence between a checkbox and an api endpoint

interface IProps extends ILocalizationProps {
    apiEndpoint: string;
    // The parent can give us this function which we use to subscribe to refresh events
    // See notes in accessibiltiyChecklist for a thorough discussion.
    subscribeToRefresh?: (queryData: () => void) => void;
    // Extra function to call before posting apiEndpoint.
    priorClickAction?: () => void;
}
interface IState {
    checked: boolean;
}
export class ApiBackedCheckbox extends React.Component<IProps, IState> {
    constructor(props) {
        super(props);
        this.state = { checked: false };
    }

    public componentDidMount() {
        this.queryData();

        if (this.props.subscribeToRefresh) {
            this.props.subscribeToRefresh(() => this.queryData());
        }
    }
    private queryData() {
        BloomApi.get(this.props.apiEndpoint, result => {
            const c = result.data as boolean;
            this.setState({ checked: c });
        });
    }

    public render() {
        return (
            <Checkbox
                className={this.props.className}
                checked={this.state.checked}
                l10nKey={this.props.l10nKey}
                onCheckChanged={c => {
                    this.setState({ checked: c });
                    if (this.props.priorClickAction) {
                        this.props.priorClickAction();
                    }
                    BloomApi.postDataWithConfig(this.props.apiEndpoint, c, {
                        headers: { "Content-Type": "application/json" }
                    });
                }}
            >
                {this.props.children}
            </Checkbox>
        );
    }
}
