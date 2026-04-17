import * as React from "react";
import Select from "react-select";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";
import * as mobxReact from "mobx-react";

import { IBloomSelectProps } from "./bloomSelectTypes";

// @mobxReact.observer means mobx will automatically track which observables this component uses
// in its render attribute function, and then re-render when they change.
// The "observable" here would be currentOption as set somewhere in a parent control.
// That is why currentOption is defined as an object reference, so changes can tie back to
// the parent control's data.
@mobxReact.observer
export class BloomReactSelect extends React.Component<IBloomSelectProps> {
    constructor(props: IBloomSelectProps) {
        super(props);

        this.props.options.map((item) => {
            if (item.l10nKey) {
                theOneLocalizationManager
                    .asyncGetTextAndSuccessInfo(
                        item.l10nKey,
                        item.label,
                        item.comment ? item.comment : "",
                        false,
                    )
                    .done((result) => {
                        item.label = result.text;
                    });
            }
        });
    }

    public render() {
        const selectedOption = this.props.currentOption.value
            ? this.props.options.filter(
                  (x) => x.value === this.props.currentOption.value,
              )[0]
            : this.props.options.filter(
                  (x) => x.value === this.props.nullOption,
              )[0];

        return (
            <Select
                value={selectedOption}
                onChange={(selected) => this.handleChange(selected)}
                options={this.props.options}
                className={this.props.className}
            />
        );
    }

    public handleChange(selectedOption) {
        if (selectedOption.value === this.props.nullOption) {
            this.props.currentOption.value = "";
        } else {
            this.props.currentOption.value = selectedOption.value;
        }
    }
}

export default BloomReactSelect;
