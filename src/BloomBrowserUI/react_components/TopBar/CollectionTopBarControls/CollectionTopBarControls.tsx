import { css } from "@emotion/react";
import * as React from "react";
import { TopBarButton } from "../../TopBarButton";
import { TeamCollectionButton } from "./TeamCollectionButton";
import { TeamCollectionStatus } from "../../../teamCollection/TeamCollectionStatus";
import {
    getBloomApiPrefix,
    post,
    useWatchApiData,
} from "../../../utils/bloomApi";
import { kBloomBlue } from "../../../bloomMaterialUITheme";
import { CollectionChooserDialog } from "../../../collection/CollectionChooserDialog";
import { showCollectionSettingsDialog } from "../../../collection/CollectionSettingsDialog";
const bloomApiPrefix = getBloomApiPrefix(false);

const kOpenCreateCollectionIcon = `${bloomApiPrefix}images/OpenCreateCollection24x24.png`;
const kSettingsIcon = `${bloomApiPrefix}images/settings24x24.png`;

const mainButtonBackground = kBloomBlue;
// Black at 80% opacity, matching the controls in WorkspaceTopRightControls
// (language menu, help, zoom) so the whole TopBar reads as one group.
const mainButtonTextColor = "rgba(0, 0, 0, 0.8)";

// Let a long (or translated) settings label wrap and stay centered instead of stretching the bar.
const settingsButtonCss = css`
    white-space: normal;
    line-height: 1.15;
    max-width: 110px;

    span {
        display: inline-block;
        text-align: center;
    }
`;

export const CollectionTopBarControls: React.FunctionComponent = () => {
    const teamCollectionStatus = useWatchApiData<TeamCollectionStatus>(
        "teamCollection/tcStatus",
        "None",
        "collection",
        "tcStatus",
    );

    // "legacy" means the winforms one. This button, its handler, and its plain-English label
    // all go away when the React CollectionSettingsDialog replaces the WinForms one.
    const handleLegacySettingsClick = React.useCallback(() => {
        post("workspace/showLegacySettingsDialog");
    }, []);

    const handleSettingsClick = React.useCallback(() => {
        showCollectionSettingsDialog();
    }, []);

    const [collectionChooserOpen, setCollectionChooserOpen] =
        React.useState(false);

    const handleOpenOrCreateClick = React.useCallback(() => {
        setCollectionChooserOpen(true);
    }, []);

    return (
        <>
            {/* The result of the two sets of flex divs is we get the TC button
           on the left and the other buttons on the right */}
            <div
                css={css`
                    display: flex;
                    align-items: flex-start;
                    justify-content: space-between;
                    padding-top: 2px;
                    width: 100%;
                `}
            >
                <TeamCollectionButton status={teamCollectionStatus} />
                <div
                    css={css`
                        display: flex;
                        gap: 10px;
                        align-items: center;

                        // The button icons are flat gray (#404040) PNG silhouettes.
                        // Recolor them to black at 80% opacity so they match this
                        // group's text and the WorkspaceTopRightControls to the right.
                        // brightness(0) blackens the flat shape (no internal detail to lose);
                        // opacity(0.8) composites it at 80%.
                        img {
                            filter: brightness(0) opacity(0.8);
                        }
                    `}
                >
                    <TopBarButton
                        iconPath={kSettingsIcon}
                        labelL10nKey="CollectionTab.SettingsButton"
                        labelEnglish="Settings"
                        onClick={handleSettingsClick}
                        backgroundColor={mainButtonBackground}
                        textColor={mainButtonTextColor}
                        cssOverrides={settingsButtonCss}
                    />
                    <TopBarButton
                        iconPath={kSettingsIcon}
                        // An empty key keeps this out of the localization files; the button is
                        // temporary and only developers and testers need to read it.
                        labelL10nKey=""
                        labelEnglish="Old Settings"
                        temporarilyDisableI18nWarning={true}
                        onClick={handleLegacySettingsClick}
                        backgroundColor={mainButtonBackground}
                        textColor={mainButtonTextColor}
                        cssOverrides={settingsButtonCss}
                    />
                    <TopBarButton
                        iconPath={kOpenCreateCollectionIcon}
                        labelL10nKey="CollectionTab.Open/CreateCollectionButton"
                        labelEnglish="Other Collection"
                        onClick={handleOpenOrCreateClick}
                        backgroundColor={mainButtonBackground}
                        textColor={mainButtonTextColor}
                    />
                </div>
            </div>
            <CollectionChooserDialog
                open={collectionChooserOpen}
                onClose={() => setCollectionChooserOpen(false)}
            />
        </>
    );
};
