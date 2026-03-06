import * as React from "react";
import { BloomTooltip } from "../../BloomToolTip";
import { ArrowDropDown, HelpOutline } from "@mui/icons-material";
import { postJson, useApiString } from "../../../utils/bloomApi";
import { TopRightMenuButton, topRightMenuArrowCss } from "./TopRightMenuButton";
import { useL10n } from "../../l10nHooks";
import Menu from "@mui/material/Menu";
import Divider from "@mui/material/Divider";
import { showAboutDialog } from "../../aboutDialog";
import { useParentFrameMenuPortal } from "../useParentFrameMenuPortal";
import { LocalizableMenuItem } from "../../localizableMenuItem";

interface IMenuItem {
    id?: string;
    label?: string;
    enabled?: boolean;
    separator?: boolean;
    onClick?: () => void;
}

export const HelpMenu: React.FunctionComponent = () => {
    const helpText = useL10n("?", "HelpMenu.Help Menu");
    const documentationText = useL10n(
        "Help...",
        "HelpMenu.DocumentationMenuItem",
    );
    const onlineHelpText = useL10n(
        "More Help (Web)",
        "HelpMenu.OnlineHelpMenuItem",
    );
    const trainingVideosText = useL10n(
        "Training Videos",
        "HelpMenu.trainingVideos",
    );
    const buildingReaderTemplatesText = useL10n(
        "Building Reader Templates",
        "HelpMenu.BuildingReaderTemplatesMenuItem",
    );
    const usingReaderTemplatesText = useL10n(
        "Using Reader Templates ",
        "HelpMenu.UsingReaderTemplatesMenuItem",
    );
    const askQuestionText = useL10n(
        "Ask a Question",
        "HelpMenu.AskAQuestionMenuItem",
    );
    const requestFeatureText = useL10n(
        "Request a Feature",
        "HelpMenu.MakeASuggestionMenuItem",
    );
    const reportProblemText = useL10n(
        "Report a Problem...",
        "HelpMenu.ReportAProblemToolStripMenuItem",
    );
    const releaseNotesText = useL10n(
        "Release Notes (Web)",
        "HelpMenu.ReleaseNotesWebMenuItem",
    );
    const checkUpdatesText = useL10n(
        "Check For New Version",
        "HelpMenu.CheckForNewVersionMenuItem",
    );
    const registrationText = useL10n(
        "Registration...",
        "HelpMenu.RegistrationMenuItem",
    );
    const websiteText = useL10n("BloomLibrary.org", "HelpMenu.WebSiteMenuItem");
    const aboutText = useL10n("About Bloom...", "HelpMenu.CreditsMenuItem");

    const uiLanguage = useApiString("currentUiLanguage", "en");

    const showIconOnly =
        helpText === "?" || ["en", "fr", "de", "es"].includes(uiLanguage);

    const {
        suppressTooltip,
        closeMenu,
        openMenuAtButton,
        suppressTooltipUntilPointerReset,
        clearTooltipSuppression,
        releaseTooltipSuppressionIfMenuClosed,
        getRootMenuProps,
        renderMenuInParentFrame,
    } = useParentFrameMenuPortal();

    const showRegistrationDialogFromWorkspaceRoot = React.useCallback(() => {
        (
            window.top as {
                workspaceBundle: {
                    showRegistrationDialogFromWorkspaceRoot: () => void;
                };
            }
        ).workspaceBundle.showRegistrationDialogFromWorkspaceRoot();
    }, []);

    const showAboutDialogFromWorkspaceRoot = React.useCallback(() => {
        const topWindow = window.top;
        const showFromRoot =
            topWindow && "workspaceBundle" in topWindow
                ? (
                      topWindow as {
                          workspaceBundle?: {
                              showRegistrationDialogFromWorkspaceRoot?:
                                  | (() => void)
                                  | undefined;
                              showAboutDialogFromWorkspaceRoot?:
                                  | (() => void)
                                  | undefined;
                          };
                      }
                  ).workspaceBundle
                : undefined;
        const showAbout = showFromRoot?.showAboutDialogFromWorkspaceRoot;
        if (typeof showAbout === "function") {
            showAbout();
            return;
        }

        showAboutDialog();
    }, []);

    const postHelpAction = React.useCallback(
        (method: string, argument?: string) => {
            postJson("workspace/helpAction", {
                method,
                argument: argument ?? null,
            });
        },
        [],
    );

    const menuItems = React.useMemo<IMenuItem[]>(
        () => [
            {
                id: "documentation",
                label: documentationText,
                onClick: () => postHelpAction("showHelp"),
            },
            {
                id: "onlineHelp",
                label: onlineHelpText,
                onClick: () =>
                    postHelpAction(
                        "safeStartInFront",
                        "https://docs.bloomlibrary.org",
                    ),
            },
            {
                id: "trainingVideos",
                label: trainingVideosText,
                onClick: () => postHelpAction("showTrainingVideos"),
            },
            {
                id: "buildingReaderTemplates",
                label: buildingReaderTemplatesText,
                onClick: () =>
                    postHelpAction(
                        "safeStartInFront",
                        "infoPage:Building and Distributing Reader Templates in Bloom.pdf",
                    ),
            },
            {
                id: "usingReaderTemplates",
                label: usingReaderTemplatesText,
                onClick: () =>
                    postHelpAction(
                        "safeStartInFront",
                        "infoPage:Using Bloom Reader Templates.pdf",
                    ),
            },
            { id: "separator-1", separator: true },
            {
                id: "askQuestion",
                label: askQuestionText,
                onClick: () =>
                    postHelpAction("safeStartInFront", "urlType:Support"),
            },
            {
                id: "requestFeature",
                label: requestFeatureText,
                onClick: () =>
                    postHelpAction(
                        "safeStartInFront",
                        "urlType:UserSuggestions",
                    ),
            },
            {
                id: "reportProblem",
                label: reportProblemText,
                onClick: () => postJson("workspace/reportProblem", {}),
            },
            { id: "separator-2", separator: true },
            {
                id: "releaseNotes",
                label: releaseNotesText,
                onClick: () =>
                    postHelpAction(
                        "safeStartInFront",
                        "https://docs.bloomlibrary.org/Release-Notes",
                    ),
            },
            {
                id: "checkForUpdates",
                label: checkUpdatesText,
                onClick: () => postJson("workspace/checkForUpdates", {}),
            },
            {
                id: "registration",
                label: registrationText,
                onClick: showRegistrationDialogFromWorkspaceRoot,
            },
            { id: "separator-3", separator: true },
            {
                id: "website",
                label: websiteText,
                onClick: () =>
                    postHelpAction("safeStartInFront", "urlType:LibrarySite"),
            },
            {
                id: "about",
                label: aboutText,
                onClick: showAboutDialogFromWorkspaceRoot,
            },
        ],
        [
            aboutText,
            askQuestionText,
            buildingReaderTemplatesText,
            checkUpdatesText,
            documentationText,
            onlineHelpText,
            postHelpAction,
            registrationText,
            releaseNotesText,
            reportProblemText,
            requestFeatureText,
            showAboutDialogFromWorkspaceRoot,
            showRegistrationDialogFromWorkspaceRoot,
            trainingVideosText,
            usingReaderTemplatesText,
            websiteText,
        ],
    );

    const onClose = React.useCallback(() => {
        closeMenu();
    }, [closeMenu]);

    const onOpen = React.useCallback(() => {
        suppressTooltipUntilPointerReset();
        const anchor = openMenuAtButton("helpMenuButton", "help-menu-parent");
        if (!anchor) {
            clearTooltipSuppression();
        }
    }, [
        clearTooltipSuppression,
        openMenuAtButton,
        suppressTooltipUntilPointerReset,
    ]);

    const handleMenuItemClick = React.useCallback(
        (item: IMenuItem) => {
            if (!item.onClick || item.enabled === false) {
                return;
            }
            item.onClick();
            onClose();
        },
        [onClose],
    );

    const menu = (
        <Menu {...getRootMenuProps(onClose)}>
            {menuItems.map((item, index) => {
                if (item.separator) {
                    return <Divider key={`separator-${index}`} />;
                }

                return (
                    <LocalizableMenuItem
                        key={`${item.id ?? item.label ?? index}`}
                        english={item.label ?? ""}
                        l10nId={null}
                        onClick={() => handleMenuItemClick(item)}
                        disabled={item.enabled === false}
                        dontGiveAffordanceForCheckbox={true}
                    />
                );
            })}
        </Menu>
    );

    const button = (
        <TopRightMenuButton
            buttonId="helpMenuButton"
            text={showIconOnly ? "" : helpText}
            onClick={onOpen}
            onMouseEnter={releaseTooltipSuppressionIfMenuClosed}
            onMouseLeave={releaseTooltipSuppressionIfMenuClosed}
            startIcon={showIconOnly ? <HelpOutline /> : undefined}
            endIcon={<ArrowDropDown css={topRightMenuArrowCss} />}
            hasText={!showIconOnly}
        />
    );

    if (!showIconOnly) {
        return (
            <>
                {button}
                {renderMenuInParentFrame(menu)}
            </>
        );
    }

    return (
        <>
            <BloomTooltip
                tip={
                    suppressTooltip
                        ? undefined
                        : { l10nKey: "HelpMenu.Help Menu" }
                }
            >
                {button}
            </BloomTooltip>
            {renderMenuInParentFrame(menu)}
        </>
    );
};
