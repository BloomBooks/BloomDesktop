import React = require("react");
import { BloomTooltip, IBloomToolTipProps } from "./BloomToolTip";

export const MustBeCheckedOut: React.FunctionComponent<{
    id: string;
    isCheckedOut: boolean;
} & IBloomToolTipProps> = props => {
    return props.isCheckedOut ? (
        <React.Fragment>{props.children}</React.Fragment>
    ) : (
        <BloomTooltip
            tooltipText="This feature requires the book to be checked out to you."
            tooltipL10nKey="CollectionTab.BookMenu.MustCheckOutTooltip"
            sideVerticalOrigin={0}
            {...props}
        />
    );
};
