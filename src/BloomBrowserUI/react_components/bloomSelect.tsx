import * as React from "react";
// import Select from "react-select";
import { MenuItem, Select } from "@material-ui/core";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";
import * as mobxReact from "mobx-react";
import { useEffect } from "react";

export interface IOption {
    value: string;
    label: string;
    l10nKey?: string;
    comment?: string;
}
export interface IProps {
    currentOption: IOption; // Only currentOption.value is used in BloomSelect.
    options: IOption[];
    nullOptionValue: string; // The IOption .value associated with not having chosen one of the real options
    className: string;
}

// @mobxReact.observer means mobx will automatically track which observables this component uses
// in its render attribute function, and then re-render when they change. The "observable" here
// would be currentOption as set somewhere in a parent control.  That is why currentOption is
// defined as "any" instead of "string", so that the object reference can tie back to the parent
// control's data.  If nothing is set as an observable, then there won't be automatic re-rendering.
// @mobxReact.observer
export const BloomSelect: React.FunctionComponent<IProps> = props => {
    // useEffect(() => {
    //     props.options.map(item => {
    //         if (item.l10nKey) {
    //             theOneLocalizationManager
    //                 .asyncGetTextAndSuccessInfo(
    //                     item.l10nKey,
    //                     item.label,
    //                     item.comment ? item.comment : "",
    //                     false
    //                 )
    //                 .done(result => {
    //                     item.label = result.text;
    //                 });
    //         }
    //     });
    // }, [props.options]);

    const [selectedValue, setSelectedValue] = React.useState<string>(
        props.currentOption.value
            ? props.options.filter(
                  x => x.value === props.currentOption.value
              )[0].value
            : props.options.filter(x => x.value === props.nullOptionValue)[0]
                  .value
    );

    function handleChange(event) {
        const newSelectedValue = event.target.value;
        if (newSelectedValue == props.nullOptionValue) {
            setSelectedValue("");
        } else {
            setSelectedValue(newSelectedValue);
        }
    }

    const menuItems = props.options.map(item => {
        return (
            <MenuItem value={item.value} key={item.value}>
                {item.label}
            </MenuItem>
        );
    });

    return (
        <Select
            value={selectedValue}
            onChange={handleChange}
            className={props.className}
        >
            {menuItems}
        </Select>
    );
};

export default BloomSelect;
