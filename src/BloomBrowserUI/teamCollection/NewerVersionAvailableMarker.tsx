import { css } from "@emotion/react";
import * as React from "react";
import UpdateIcon from "@mui/icons-material/Update";
import { BloomTooltip } from "../react_components/BloomToolTip";
import { kBloomBlue } from "../bloomMaterialUITheme";

// A subtle marker overlaid on a book's thumbnail in the collection tab's book list (BookButton.tsx),
// shown for a cloud Team Collection book that isn't checked out to anyone but has a newer version
// in the repo than what's on this computer (same condition as the "updatesAvailable" state in
// TeamCollectionBookStatusPanel.tsx, which shows for the currently-selected book). Positioned
// top-right of the thumbnail so it doesn't collide with the holder-avatar overlay (top-left of
// the book button) or BookOnBlorgBadge (bottom-right of the thumbnail).
export const NewerVersionAvailableMarker: React.FunctionComponent<{
    show: boolean;
}> = (props) => {
    if (!props.show) {
        return null;
    }
    return (
        <div
            css={css`
                position: absolute;
                top: -3px;
                right: -3px;
                z-index: 1000;
            `}
        >
            <BloomTooltip
                placement="top"
                tip={{
                    l10nKey: "TeamCollection.UpdatesAvailableForBook",
                    english: "A newer version of this book is available",
                }}
            >
                <UpdateIcon
                    data-testid="newer-version-available-marker"
                    css={css`
                        color: ${kBloomBlue};
                        background-color: white;
                        border-radius: 50%;
                        font-size: 16px;
                    `}
                />
            </BloomTooltip>
        </div>
    );
};
