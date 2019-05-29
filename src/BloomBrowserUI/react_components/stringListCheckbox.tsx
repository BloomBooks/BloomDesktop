import * as React from "react";
import * as mobxReact from "mobx-react";
import { Checkbox } from "./checkbox";
import { ILocalizationProps, LocalizableElement } from "./l10nComponents";

interface IProps extends ILocalizationProps {
    list: string;
    onChange: (s: string) => void;
    itemName: string;
    // supply this for a tristate list, where off must be represented by an item in the list
    // and indeterminate is represented by a total absence from the list.
    tristateItemOffName?: string;
}

// A StringListCheckbox is a control that can add and remove a value from a string of comma-separated values.
// It also has the capability to support a 3-state scenario where the user can say something is true (e.g. "motionHazard"),
// something is not true, and just be silent on the matter. In the case of the "something is not true",
// a "tristateItemOffName" is used, e.g. "noMotionHazard".

@mobxReact.observer
export class StringListCheckbox extends LocalizableElement<IProps, {}> {
    public constructor(props: IProps) {
        super(props);
    }

    private getNewList(buttonState: boolean): string {
        const parts: string[] =
            this.props.list.trim().length > 0 ? this.props.list.split(",") : [];

        // first, just remove anything related to this checkbox from the list
        const indexOfOnName = parts.indexOf(this.props.itemName);
        if (indexOfOnName > -1) {
            parts.splice(indexOfOnName, 1);
        }
        if (this.props.tristateItemOffName) {
            const indexOfOffName = parts.indexOf(
                this.props.tristateItemOffName
            );
            if (indexOfOffName > -1) {
                parts.splice(indexOfOffName, 1);
            }
        }

        if (buttonState) {
            parts.push(this.props.itemName);
        }
        // if we're in on/off/unknown mode
        if (this.props.tristateItemOffName && buttonState === false) {
            parts.push(this.props.tristateItemOffName);
        }

        return parts.length > 0 ? parts.join(",") : "";
    }

    private getCheckStatus(): boolean | undefined {
        const parts = this.props.list.split(",");
        if (parts.indexOf(this.props.itemName) > -1) {
            return true;
        }
        if (
            this.props.tristateItemOffName &&
            parts.indexOf(this.props.tristateItemOffName) > -1
        ) {
            return false;
        }
        return undefined; // indeterminate
    }

    public render() {
        const checkStatus = this.getCheckStatus();
        return (
            <Checkbox
                tristate={this.props.tristateItemOffName !== undefined}
                l10nKey={this.props.l10nKey}
                alreadyLocalized={this.props.alreadyLocalized}
                checked={checkStatus}
                onCheckChanged={checked => {
                    this.props.onChange(this.getNewList(checked));
                }}
            >
                {this.props.children}
            </Checkbox>
        );
    }
}
