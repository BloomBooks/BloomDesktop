import * as React from "react";
import { ILocalizationProps } from "./l10nComponents";
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
    // Extra function called when the checked value changes, including when we
    // retrieve it from the backing API. (This means it MIGHT not actually have changed.)
    onCheckChanged?: (boolean) => void;
}
interface IState {
    checked: boolean;
}

export class ApiBackedCheckbox extends React.Component<IProps, IState> {
    public readonly state: IState = {
        checked: false
    };

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
            if (this.props.onCheckChanged) {
                this.props.onCheckChanged(c);
            }
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
                    if (this.props.onCheckChanged) {
                        this.props.onCheckChanged(c);
                    }
                }}
            >
                {this.props.children}
            </Checkbox>
        );
    }
}
