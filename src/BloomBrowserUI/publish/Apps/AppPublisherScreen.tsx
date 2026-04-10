import { css } from "@emotion/react";
import FolderOpenIcon from "@mui/icons-material/FolderOpen";
import PrecisionManufacturingIcon from "@mui/icons-material/PrecisionManufacturing";
import SettingsIcon from "@mui/icons-material/Settings";
import {
    Step,
    StepContent,
    StepLabel,
    Tooltip,
    Typography,
} from "@mui/material";
import * as React from "react";
import { BloomStepper } from "../../react_components/BloomStepper";
import HelpLink from "../../react_components/helpLink";
import { Link } from "../../react_components/link";
import BloomButton from "../../react_components/bloomButton";
import { useL10n } from "../../react_components/l10nHooks";
import {
    HelpGroup,
    PublishPanel,
    SettingsPanel,
} from "../commonPublish/PublishScreenBaseComponents";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import { ActionLogAccordion } from "./ActionLogAccordion";
import {
    ActualApkSizeText,
    EstimatedAppSizeIndicator,
} from "./AppSizeIndicator";
import { AppBuilderSettingsDialog } from "./AppBuilderSettingsDialog";
import { AppBuilderChooseBooksDialog } from "./AppBuilderChooseBooksDialog";
import {
    arePrepareStepsComplete,
    getPrepareStepIdForStage,
    getProgressStageLabel,
    kAppBuilderWebSocketContext,
} from "./appBuilderShared";
import { InlineProgressStatus } from "./InlineProgressStatus";
import { PrepareAppStepper } from "./PrepareAppStepper";
import { useAppBuilderPublisherScreen } from "./useAppBuilderPublisherScreen";

const apkToPhoneIconUrl = new URL(
    "../PublishTab/ApkToPhone.svg",
    import.meta.url,
).toString();

const AppActionButton: React.FunctionComponent<{
    enabled: boolean;
    l10nKey: string;
    onClick: () => void;
    tooltip?: string;
    children: React.ReactNode;
    size?: "small" | "medium" | "large";
    variant?: "text" | "outlined" | "contained";
    iconBeforeText?: React.ReactNode;
}> = (props) => {
    return (
        <Tooltip title={props.tooltip} placement="top">
            <span
                css={css`
                    display: inline-flex;
                `}
            >
                <BloomButton
                    enabled={props.enabled}
                    l10nKey={props.l10nKey}
                    onClick={props.onClick}
                    hasText={true}
                    size={props.size}
                    variant={props.variant ?? "contained"}
                    iconBeforeText={props.iconBeforeText}
                    sx={{ textTransform: "none" }}
                >
                    {props.children}
                </BloomButton>
            </span>
        </Tooltip>
    );
};

// Keep this component mostly declarative. The hook owns websocket/API state so the JSX can stay focused on the workflow.
const AppPublisherScreenContents: React.FunctionComponent<{
    isActive: boolean;
}> = (props) => {
    const screenState = useAppBuilderPublisherScreen(props.isActive);
    const [showSettingsDialog, setShowSettingsDialog] = React.useState(false);
    const [showChooseBooksDialog, setShowChooseBooksDialog] =
        React.useState(false);
    const setupTooltip = useL10n(
        "Create the Reading App Builder project in this collection's Bloom App Data folder.",
        "PublishTab.Apps.Setup.TooltipBloomAppData",
    );
    const setupDoneTooltip = useL10n(
        "The Reading App Builder project is already set up for this collection.",
        "PublishTab.Apps.Setup.DoneTooltip",
    );
    const chooseBooksTooltip = useL10n(
        "Choose which books to include in the app and the order they should appear.",
        "PublishTab.Apps.ChooseBooks.Tooltip",
    );
    const settingsTooltip = useL10n(
        "Edit the Reading App Builder app settings for this collection.",
        "PublishTab.Apps.SettingsButton.Tooltip",
    );
    const buildTooltip = useL10n(
        "Build a new APK from the current settings and selected books.",
        "PublishTab.Apps.Build.Tooltip",
    );
    const buildDoneTooltip = useL10n(
        "The APK is current. Try on phone will use the latest build.",
        "PublishTab.Apps.Build.DoneTooltip",
    );
    const setupRequiredTooltip = useL10n(
        "Run Setup before using this step.",
        "PublishTab.Apps.SetupRequiredTooltip",
    );
    const buildNeedsSetupTooltip = useL10n(
        "Run Setup before building the app.",
        "PublishTab.Apps.Build.NeedsSetupTooltip",
    );
    const tryOnPhoneTooltip = useL10n(
        "Load and run the app on your phone. First ble USB Debugging on the phone and connect it with a USB cable.",
        "PublishTab.Apps.TryOnPhone.Tooltip",
    );
    const showApkInExplorer = useL10n(
        "Show App (.apk) in File Explorer",
        "PublishTab.Apps.ShowApkInFileExplorer",
    );
    const setupButtonLabel = useL10n(
        "Prepare",
        "PublishTab.Apps.PrepareButton",
    );
    const settingsButtonLabel = useL10n(
        "Customize...",
        "PublishTab.Apps.CustomizeButton",
    );
    const customizeValidationMessage = useL10n(
        "Some required settings are missing or invalid.",
        "PublishTab.Apps.CustomizeButton.ValidationMessage",
    );
    const preparingWorkspaceLabel = useL10n(
        "Preparing workspace",
        "PublishTab.Apps.Progress.PreparingWorkspace",
    );
    const preparingBuildLabel = useL10n(
        "Preparing build",
        "PublishTab.Apps.Progress.PreparingBuild",
    );
    const exportingBloompubsLabel = useL10n(
        "Creating BloomPUBs",
        "PublishTab.Apps.Progress.ExportingBloompubs",
    );
    const checkingDeviceLabel = useL10n(
        "Checking for connected phone",
        "PublishTab.Apps.Progress.CheckingDevice",
    );
    const generatingSigningKeyLabel = useL10n(
        "Generating signing key",
        "PublishTab.Apps.Progress.GeneratingSigningKey",
    );
    const creatingProjectLabel = useL10n(
        "Creating Reading App Builder project",
        "PublishTab.Apps.Progress.CreatingProject",
    );
    const updatingProjectLabel = useL10n(
        "Updating Reading App Builder project",
        "PublishTab.Apps.Progress.UpdatingProject",
    );
    const buildingAndroidAppLabel = useL10n(
        "Building Android app",
        "PublishTab.Apps.Progress.BuildingAndroidApp",
    );
    const finalizingApkLabel = useL10n(
        "Finalizing APK",
        "PublishTab.Apps.Progress.FinalizingApk",
    );
    const installingOnPhoneLabel = useL10n(
        "Installing app on phone",
        "PublishTab.Apps.Progress.InstallingOnPhone",
    );
    const launchingOnPhoneLabel = useL10n(
        "Launching app on phone",
        "PublishTab.Apps.Progress.LaunchingOnPhone",
    );
    const progressCompleteLabel = useL10n(
        "Done",
        "PublishTab.Apps.Progress.Complete",
    );
    const installerAvailableLabel = useL10n(
        "Get installer",
        "PublishTab.Apps.PrepareStepper.InstallerAvailable",
    );
    const rabInstalledLabel = useL10n(
        "Run installer",
        "PublishTab.Apps.PrepareStepper.RabInstalled",
    );
    const buildToolsInstalledLabel = useL10n(
        "Install build tools",
        "PublishTab.Apps.PrepareStepper.BuildToolsInstalled",
    );
    const projectCreatedLabel = useL10n(
        "Create project",
        "PublishTab.Apps.PrepareStepper.ProjectCreated",
    );
    const rabInstalled = screenState.status.rabInstalled;
    const prepareIsReady = arePrepareStepsComplete(
        screenState.status.prepareSteps,
    );
    const buildIsNeeded = screenState.buildIsNeeded;
    const busyAction = screenState.busyAction;
    const apkIsCurrent = screenState.status.apkExists && !buildIsNeeded;
    const canRunSetup = !busyAction && !prepareIsReady;
    const canUseConfiguredProject = prepareIsReady && !busyAction;
    const canRunBuild = !busyAction && screenState.hasRequiredBuildSettings;
    const canUseCurrentApk = apkIsCurrent && !busyAction;
    const activePrepareStepId = getPrepareStepIdForStage(
        busyAction,
        screenState.progressStageCode,
    );
    const prepareSteps = screenState.status.prepareSteps.map((step) => ({
        ...step,
        label:
            step.id === "installer-available"
                ? installerAvailableLabel
                : step.id === "rab-installed"
                  ? rabInstalledLabel
                  : step.id === "build-tools-installed"
                    ? buildToolsInstalledLabel
                    : projectCreatedLabel,
    }));
    const currentProgressStageLabel = getProgressStageLabel(
        busyAction,
        screenState.progressStageCode,
        {
            preparingWorkspace: preparingWorkspaceLabel,
            preparingBuild: preparingBuildLabel,
            checkingDevice: checkingDeviceLabel,
            exportingBloompubs: exportingBloompubsLabel,
            generatingSigningKey: generatingSigningKeyLabel,
            creatingProject: creatingProjectLabel,
            updatingProject: updatingProjectLabel,
            buildingAndroidApp: buildingAndroidAppLabel,
            finalizingApk: finalizingApkLabel,
            installingOnPhone: installingOnPhoneLabel,
            launchingOnPhone: launchingOnPhoneLabel,
            complete: progressCompleteLabel,
        },
    );
    const buildTooltipToShow = !prepareIsReady
        ? buildNeedsSetupTooltip
        : buildIsNeeded
          ? buildTooltip
          : buildDoneTooltip;
    const setupTooltipToShow = prepareIsReady ? setupDoneTooltip : setupTooltip;

    return (
        <>
            <PublishPanel
                css={css`
                    height: 100%;
                    min-height: 0;
                    box-sizing: border-box;
                    gap: 16px;
                    overflow-x: hidden;
                    overflow-y: auto;
                `}
            >
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        gap: 12px;
                        flex: 0 0 auto;
                        max-width: 900px;
                    `}
                >
                    {/* The stepper mirrors the real RAB workflow so each action keeps its own status and log surface nearby. */}
                    <BloomStepper
                        orientation="vertical"
                        areStepsAlwaysEnabled={true}
                        css={css`
                            .MuiStepContent-root {
                                padding-bottom: 0;
                            }

                            .MuiStepLabel-label {
                                font-weight: 600;
                            }
                        `}
                    >
                        <Step expanded={true} completed={false}>
                            <StepLabel>
                                <AppActionButton
                                    enabled={canRunSetup}
                                    l10nKey="PublishTab.Apps.PrepareButton"
                                    onClick={() =>
                                        screenState.runAction("setup")
                                    }
                                    size="large"
                                    tooltip={setupTooltipToShow}
                                >
                                    {setupButtonLabel}
                                </AppActionButton>
                            </StepLabel>
                            <StepContent>
                                <PrepareAppStepper
                                    steps={prepareSteps}
                                    activeStepId={activePrepareStepId}
                                    isBusy={busyAction === "setup"}
                                />
                                <ActionLogAccordion
                                    controller={screenState.setupLog}
                                    isActive={busyAction === "setup"}
                                    webSocketContext={
                                        busyAction === "setup"
                                            ? kAppBuilderWebSocketContext
                                            : undefined
                                    }
                                    dataTestId="setup-log-accordion"
                                />
                            </StepContent>
                        </Step>
                        <Step expanded={true} completed={false}>
                            <StepLabel>
                                <AppActionButton
                                    enabled={canUseConfiguredProject}
                                    l10nKey="PublishTab.Apps.ChooseBooks"
                                    onClick={() =>
                                        setShowChooseBooksDialog(true)
                                    }
                                    size="large"
                                    variant="contained"
                                    tooltip={
                                        prepareIsReady
                                            ? chooseBooksTooltip
                                            : setupRequiredTooltip
                                    }
                                >
                                    Choose Books...
                                </AppActionButton>
                            </StepLabel>
                            <StepContent>
                                {screenState.status.trackedBooks.length > 0 &&
                                    screenState.sizeEstimates.books.length >
                                        0 && (
                                        <EstimatedAppSizeIndicator
                                            sizeEstimates={
                                                screenState.sizeEstimates
                                            }
                                            books={screenState.status.trackedBooks.map(
                                                (book) => ({
                                                    bookId: book.bookId,
                                                    folderPath: book.folderPath,
                                                    title: book.title,
                                                }),
                                            )}
                                        />
                                    )}
                            </StepContent>
                        </Step>
                        <Step expanded={true} completed={false}>
                            <StepLabel>
                                <AppActionButton
                                    enabled={canUseConfiguredProject}
                                    l10nKey="PublishTab.Apps.CustomizeButton"
                                    onClick={() => setShowSettingsDialog(true)}
                                    size="large"
                                    variant="contained"
                                    tooltip={
                                        prepareIsReady
                                            ? settingsTooltip
                                            : setupRequiredTooltip
                                    }
                                    iconBeforeText={<SettingsIcon />}
                                >
                                    {settingsButtonLabel}
                                </AppActionButton>
                            </StepLabel>
                            <StepContent>
                                {canUseConfiguredProject &&
                                    !screenState.hasRequiredBuildSettings && (
                                        <Typography
                                            css={css`
                                                color: #c62828;
                                                font-size: 0.9rem;
                                                line-height: 1.35;
                                                max-width: 340px;
                                            `}
                                        >
                                            {customizeValidationMessage}
                                        </Typography>
                                    )}
                            </StepContent>
                        </Step>
                        <Step expanded={true} completed={false}>
                            <StepLabel>
                                <AppActionButton
                                    enabled={
                                        prepareIsReady &&
                                        buildIsNeeded &&
                                        canRunBuild
                                    }
                                    l10nKey="PublishTab.Apps.Build"
                                    onClick={() =>
                                        screenState.runAction("build")
                                    }
                                    size="large"
                                    tooltip={buildTooltipToShow}
                                    iconBeforeText={
                                        <PrecisionManufacturingIcon />
                                    }
                                >
                                    Build
                                </AppActionButton>
                            </StepLabel>
                            <StepContent>
                                <InlineProgressStatus
                                    showProgress={
                                        busyAction === "build" &&
                                        !!currentProgressStageLabel
                                    }
                                    progressLabel={
                                        busyAction === "build"
                                            ? currentProgressStageLabel
                                            : undefined
                                    }
                                    progressPercent={
                                        screenState.progressPercent
                                    }
                                />
                                <ActionLogAccordion
                                    controller={screenState.buildLog}
                                    isActive={busyAction === "build"}
                                    webSocketContext={
                                        busyAction === "build"
                                            ? kAppBuilderWebSocketContext
                                            : undefined
                                    }
                                    dataTestId="build-log-accordion"
                                />
                            </StepContent>
                        </Step>
                        <Step expanded={true} completed={false}>
                            <StepLabel>
                                <div
                                    css={css`
                                        display: flex;
                                        flex-wrap: wrap;
                                        gap: 12px;
                                        align-items: flex-start;
                                    `}
                                >
                                    <AppActionButton
                                        enabled={canUseCurrentApk}
                                        l10nKey="PublishTab.Apps.TryOnPhone"
                                        onClick={() =>
                                            screenState.runAction("install")
                                        }
                                        size="large"
                                        tooltip={tryOnPhoneTooltip}
                                        iconBeforeText={
                                            <img
                                                src={apkToPhoneIconUrl}
                                                width={19}
                                                height={14}
                                                alt=""
                                            />
                                        }
                                    >
                                        Try on phone
                                    </AppActionButton>
                                    <AppActionButton
                                        enabled={canUseCurrentApk}
                                        l10nKey="PublishTab.Apps.ShowApkInFileExplorer"
                                        onClick={
                                            screenState.showApkInExplorerInShell
                                        }
                                        size="large"
                                        variant="text"
                                        iconBeforeText={<FolderOpenIcon />}
                                    >
                                        {showApkInExplorer}
                                    </AppActionButton>
                                </div>
                            </StepLabel>
                            <StepContent>
                                <InlineProgressStatus
                                    showProgress={
                                        busyAction === "install" &&
                                        !!currentProgressStageLabel
                                    }
                                    progressLabel={
                                        busyAction === "install"
                                            ? currentProgressStageLabel
                                            : undefined
                                    }
                                    progressPercent={
                                        screenState.progressPercent
                                    }
                                />
                                {canUseCurrentApk &&
                                    screenState.status.apkExists &&
                                    (screenState.status.apkSizeBytes ?? 0) >
                                        0 && (
                                        <ActualApkSizeText
                                            apkSizeBytes={
                                                screenState.status
                                                    .apkSizeBytes ?? 0
                                            }
                                        />
                                    )}
                                <ActionLogAccordion
                                    controller={screenState.installLog}
                                    isActive={busyAction === "install"}
                                    webSocketContext={
                                        busyAction === "install"
                                            ? kAppBuilderWebSocketContext
                                            : undefined
                                    }
                                    dataTestId="install-log-accordion"
                                />
                            </StepContent>
                        </Step>
                    </BloomStepper>
                </div>
            </PublishPanel>
            {showSettingsDialog && (
                <AppBuilderSettingsDialog
                    appDefPath={screenState.status.appDefPath ?? ""}
                    canOpenInRab={
                        screenState.status.projectExists && rabInstalled
                    }
                    onClose={() => setShowSettingsDialog(false)}
                    onSaved={() => {
                        screenState.markConfigurationChanged();
                    }}
                />
            )}
            {showChooseBooksDialog && (
                <AppBuilderChooseBooksDialog
                    currentBooks={screenState.status.trackedBooks}
                    sizeEstimates={screenState.sizeEstimates}
                    onClose={() => setShowChooseBooksDialog(false)}
                    onSaved={() => {
                        screenState.markConfigurationChanged();
                    }}
                />
            )}
        </>
    );
};

export const AppPublisherScreen: React.FunctionComponent<{
    isActive: boolean;
}> = (props) => {
    const optionsPanel = (
        <SettingsPanel>
            <div
                css={css`
                    margin-top: auto;
                `}
            />
            <HelpGroup>
                <HelpLink
                    helpId="Tasks/Publish_tasks/Publish_tasks_overview.htm"
                    l10nKey="PublishTab.TasksOverview"
                >
                    Publish tab tasks overview
                </HelpLink>
                <Link
                    href="https://software.sil.org/readingappbuilder"
                    l10nKey="PublishTab.Apps.ReadingAppBuilderSite"
                >
                    Reading App Builder website
                </Link>
            </HelpGroup>
        </SettingsPanel>
    );

    return (
        <Typography
            component={"div"}
            css={css`
                height: 100%;
            `}
        >
            <PublishScreenTemplate
                bannerTitleEnglish="Build Android Apps!"
                bannerTitleL10nId="PublishTab.Apps.BannerTitle"
                bannerDescriptionMarkdown="Create an app that you can install on your Android phone, share with others, and publish on the Google Play Store."
                optionsPanelContents={optionsPanel}
            >
                <AppPublisherScreenContents isActive={props.isActive} />
            </PublishScreenTemplate>
        </Typography>
    );
};
