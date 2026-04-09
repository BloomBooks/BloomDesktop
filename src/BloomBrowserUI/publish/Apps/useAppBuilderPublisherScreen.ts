import * as React from "react";
import { get, postData, postJson } from "../../utils/bloomApi";
import {
    IBloomWebSocketProgressEvent,
    useSubscribeToWebSocketForEvent,
    useSubscribeToWebSocketForStringMessage,
} from "../../utils/WebSocketManager";
import {
    IActionLogController,
    useActionLogController,
} from "./ActionLogAccordion";
import {
    defaultStatus,
    getProgressStageCodeFromMessage,
    IAppBuilderStatus,
    IAppBuilderSizeEstimatesApi,
    kAppBuilderWebSocketContext,
    normalizeSizeEstimates,
    normalizeStatus,
    AppBuilderAction,
} from "./appBuilderShared";
import {
    defaultSettings,
    hasRequiredBuildSettings,
    IAppBuilderAppSettings,
    refreshAppBuilderSettings,
} from "./appBuilderAppDef";
import { IAppSizeEstimates } from "./AppSizeIndicator";

export interface IAppBuilderPublisherScreenState {
    setupLog: IActionLogController;
    buildLog: IActionLogController;
    installLog: IActionLogController;
    status: IAppBuilderStatus;
    buildIsNeeded: boolean;
    hasRequiredBuildSettings: boolean;
    busyAction?: AppBuilderAction;
    progressPercent: number;
    progressStageCode?: string;
    sizeEstimates: IAppSizeEstimates;
    runAction: (action: AppBuilderAction) => void;
    showApkInExplorerInShell: () => void;
    markConfigurationChanged: () => void;
}

export function useAppBuilderPublisherScreen(
    isActive: boolean,
): IAppBuilderPublisherScreenState {
    const setupLog = useActionLogController();
    const buildLog = useActionLogController();
    const installLog = useActionLogController();
    const [status, setStatus] =
        React.useState<IAppBuilderStatus>(defaultStatus);
    const [settings, setSettings] =
        React.useState<IAppBuilderAppSettings>(defaultSettings);
    const [busyAction, setBusyAction] = React.useState<AppBuilderAction>();
    const [progressPercent, setProgressPercent] = React.useState(0);
    const [progressStageCode, setProgressStageCode] = React.useState<string>();
    // Dialog saves can land before the status endpoint reflects the new build signature.
    // Track that local dirtiness so the Build button stays honest until the next refresh.
    const [pendingBuildNeeded, setPendingBuildNeeded] = React.useState(false);
    const [rawSizeEstimates, setRawSizeEstimates] =
        React.useState<IAppBuilderSizeEstimatesApi>({});
    const sizeEstimates = normalizeSizeEstimates(rawSizeEstimates);

    useSubscribeToWebSocketForStringMessage(
        kAppBuilderWebSocketContext,
        "stage",
        (stage) => {
            setProgressStageCode(stage);
        },
    );
    useSubscribeToWebSocketForEvent(
        kAppBuilderWebSocketContext,
        "percent",
        (event) => {
            const progressEvent = event as IBloomWebSocketProgressEvent;
            if (progressEvent.percent !== undefined) {
                setProgressPercent(progressEvent.percent);
            }
        },
    );
    // Some progress transitions still arrive only as log lines, so keep a message-based fallback for stage labels.
    useSubscribeToWebSocketForEvent(
        kAppBuilderWebSocketContext,
        "message",
        (event) => {
            const progressEvent = event as IBloomWebSocketProgressEvent;
            const stageCode = getProgressStageCodeFromMessage(
                progressEvent.message,
            );
            if (stageCode) {
                setProgressStageCode(stageCode);
            }
        },
    );

    function refreshSizeEstimates(): void {
        get("publish/rab/size-estimates", (result) => {
            setRawSizeEstimates(result.data);
        });
    }

    function refreshSettings(appDefPath?: string): void {
        refreshAppBuilderSettings(setSettings, appDefPath);
    }

    function refreshStatus(): void {
        get("publish/rab/status", (result) => {
            const nextStatus = normalizeStatus(result.data);
            setStatus(nextStatus);
            refreshSettings(nextStatus.appDefPath);
        });
    }

    // This effect is warranted because tab activation is an external UI lifecycle boundary,
    // and we need to re-read the RAB project whenever the Apps screen becomes active.
    React.useEffect(() => {
        if (!isActive) {
            return;
        }

        refreshStatus();
        refreshSizeEstimates();
    }, [isActive]);

    // This effect is warranted because returning from Reading App Builder restores browser focus,
    // and that is our signal to refresh from the appDef after external edits.
    React.useEffect(() => {
        const handleWindowFocus = () => {
            if (!isActive) {
                return;
            }

            refreshStatus();
            refreshSizeEstimates();
        };

        window.addEventListener("focus", handleWindowFocus);
        return () => {
            window.removeEventListener("focus", handleWindowFocus);
        };
    }, [isActive]);

    function getActionLogController(
        action: AppBuilderAction,
    ): IActionLogController {
        switch (action) {
            case "setup":
                return setupLog;
            case "build":
                return buildLog;
            case "install":
                return installLog;
        }
    }

    function runAction(action: AppBuilderAction): void {
        if (action === "build" && !hasRequiredBuildSettings(settings)) {
            return;
        }

        getActionLogController(action).clear();

        // Reset visible progress before starting a new backend operation so reused accordions never show stale state.
        setProgressPercent(0);
        setProgressStageCode(
            action === "setup"
                ? "checking-installer"
                : action === "build"
                  ? "preparing-build"
                  : action === "install"
                    ? "checking-device"
                    : undefined,
        );
        setBusyAction(action);
        postData(
            `publish/rab/${action}`,
            {},
            () => {
                setBusyAction(undefined);
                setProgressPercent(0);
                setProgressStageCode(undefined);
                if (action === "setup" || action === "build") {
                    setPendingBuildNeeded(false);
                    refreshSizeEstimates();
                }
                refreshStatus();
            },
            () => {
                setBusyAction(undefined);
                setProgressPercent(0);
                setProgressStageCode(undefined);
                refreshStatus();
            },
        );
    }

    function showApkInExplorerInShell(): void {
        if (!status.apkPath) {
            return;
        }

        void postJson("fileIO/showInFolder", { folderPath: status.apkPath });
    }

    function markConfigurationChanged(): void {
        setPendingBuildNeeded(true);
        refreshStatus();
    }

    return {
        setupLog,
        buildLog,
        installLog,
        status,
        buildIsNeeded: status.buildNeeded || pendingBuildNeeded,
        hasRequiredBuildSettings: hasRequiredBuildSettings(settings),
        busyAction,
        progressPercent,
        progressStageCode,
        sizeEstimates,
        runAction,
        showApkInExplorerInShell,
        markConfigurationChanged,
    };
}
