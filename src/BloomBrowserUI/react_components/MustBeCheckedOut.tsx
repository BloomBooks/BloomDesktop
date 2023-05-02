import React = require("react");
import { SelectedBookContext } from "../app/SelectedBookContext";
import { BloomTooltip, IBloomToolTipProps } from "./BloomToolTip";

export const MustBeCheckedOut: React.FunctionComponent<{
    placement?: IBloomToolTipProps["placement"];
    children: React.ReactNode;
}> = props => {
    const selectedBookInfo = React.useContext(SelectedBookContext);

    return selectedBookInfo.saveable ? (
        <React.Fragment>{props.children}</React.Fragment>
    ) : (
        <BloomTooltip
            showDisabled={true}
            tipWhenDisabled={{
                english:
                    "This feature requires the book to be checked out to you.",
                l10nKey: "CollectionTab.BookMenu.MustCheckOutTooltip"
            }}
            {...props}
        ></BloomTooltip>
    );
};
