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
import { IAppSizeEstimates } from "./AppSizeIndicator";

export interface IAppBuilderPublisherScreenState {
    setupLog: IActionLogController;
    buildLog: IActionLogController;
    installLog: IActionLogController;
    status: IAppBuilderStatus;
    buildIsNeeded: boolean;
    busyAction?: AppBuilderAction;
    progressPercent: number;
    progressStageCode?: string;
    sizeEstimates: IAppSizeEstimates;
    runAction: (action: AppBuilderAction) => void;
    showApkInExplorerInShell: () => void;
    markConfigurationChanged: () => void;
}

export function useAppBuilderPublisherScreen(): IAppBuilderPublisherScreenState {
    const setupLog = useActionLogController();
    const buildLog = useActionLogController();
    const installLog = useActionLogController();
    const [status, setStatus] =
        React.useState<IAppBuilderStatus>(defaultStatus);
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

    function refreshStatus(): void {
        get("publish/rab/status", (result) => {
            setStatus(normalizeStatus(result.data));
        });
    }

    // Load size estimates once on mount so the book picker can render against the current collection.
    React.useEffect(() => {
        refreshSizeEstimates();
    }, []);

    // Load the current RAB status once on mount so the button state reflects the collection's rab folder.
    React.useEffect(() => {
        refreshStatus();
    }, []);

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
        busyAction,
        progressPercent,
        progressStageCode,
        sizeEstimates,
        runAction,
        showApkInExplorerInShell,
        markConfigurationChanged,
    };
}
