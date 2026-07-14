import { css } from "@emotion/react";
import * as React from "react";
import { useRef, useState } from "react";
import ShareIcon from "@mui/icons-material/Share";
import Popover from "@mui/material/Popover";
import BloomButton from "../react_components/bloomButton";
import { SharingPanel } from "./SharingPanel";
import { useSharingLoginState } from "./sharingApi";
import {
    isCloudTeamCollection,
    useCloudCollectionId,
    useIsTeamCollectionAdmin,
    useTeamCollectionCapabilities,
} from "./teamCollectionApi";

// The Share button shown beside the Team Collection status button in the collection tab.
// Cloud Team Collections only: it branches on capability (never on concrete backend type), so
// folder Team Collections render nothing here and see zero UI change. Clicking it opens the
// same SharingPanel used in Team Collection settings, anchored under the button; SharingPanel
// itself already renders the admin manage view or the member read-only view depending on the
// `isAdmin` prop.
export const ShareButton: React.FunctionComponent = () => {
    const capabilities = useTeamCollectionCapabilities();
    const collectionId = useCloudCollectionId();
    const isAdmin = useIsTeamCollectionAdmin();
    const { email } = useSharingLoginState();
    const anchorRef = useRef<HTMLDivElement | null>(null);
    const [open, setOpen] = useState(false);

    if (!isCloudTeamCollection(capabilities)) {
        return null;
    }

    return (
        <div
            ref={anchorRef}
            css={css`
                display: inline-block;
            `}
        >
            <BloomButton
                id="teamCollectionShareButton"
                l10nKey="TeamCollection.Sharing.ShareButton"
                temporarilyDisableI18nWarning={true}
                enabled={true}
                hasText={true}
                variant="outlined"
                iconBeforeText={<ShareIcon fontSize="small" />}
                onClick={() => setOpen(true)}
            >
                Share
            </BloomButton>
            <Popover
                open={open}
                anchorEl={anchorRef.current}
                onClose={() => setOpen(false)}
                anchorOrigin={{ vertical: "bottom", horizontal: "left" }}
            >
                <div
                    css={css`
                        padding: 12px;
                        min-width: 320px;
                    `}
                >
                    <SharingPanel
                        collectionId={collectionId}
                        currentUserEmail={email ?? ""}
                        isAdmin={isAdmin}
                    />
                </div>
            </Popover>
        </div>
    );
};
