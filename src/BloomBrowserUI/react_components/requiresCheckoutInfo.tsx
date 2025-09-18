import * as React from "react";
import { useCanModifyCurrentBook } from "../utils/bloomApi";
import { InfoTooltip } from "./icons/InfoTooltip";

// Displays an info icon if the current book needs to be checked out before it
// can be edited. When hovered over (or clicked) it displays a message indicating that
// the user needs to check out the book to use (some typically nearby disabled) control.
export const RequiresCheckoutInfo: React.FunctionComponent<{
    className?: string;
}> = (props) => {
    const canModifyCurrentBook = useCanModifyCurrentBook();
    return (
        <React.Fragment>
            {canModifyCurrentBook || (
                <InfoTooltip
                    className={props.className} // pick up any css from the emotion props of the caller
                    color="gray"
                    size="15px"
                    l10nKey="TeamCollection.CheckoutToEdit"
                    temporarilyDisableI18nWarning={true}
                >
                    Check out the book to use this control
                </InfoTooltip>
            )}
        </React.Fragment>
    );
};
