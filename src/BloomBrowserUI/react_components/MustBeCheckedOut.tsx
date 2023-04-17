import React = require("react");
import { BloomTooltip, IBloomToolTipProps } from "./BloomToolTip";
import { SelectedBookContext } from "../app/SelectedBookContext";

export const MustBeCheckedOut: React.FunctionComponent<React.PropsWithChildren<
    IBloomToolTipProps
>> = props => {
    const selectedBookInfo = React.useContext(SelectedBookContext);

    return selectedBookInfo.saveable ? (
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
