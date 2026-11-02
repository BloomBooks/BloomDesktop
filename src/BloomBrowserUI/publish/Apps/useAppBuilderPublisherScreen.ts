import * as React from "react";
import { get, getWithPromise, post, postJson } from "../../utils/bloomApi";
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
    IAppBuilderStatusApi,
    IAppBuilderSizeEstimatesApi,
    kAppBuilderWebSocketContext,
    kAppBuilderActionCompleteEventId,
    kAppBuilderSizeEstimatesChangedEventId,
    normalizeSizeEstimates,
    normalizeStatus,
    AppBuilderAction,
} from "./appBuilderShared";
import {
    defaultSettings,
    fetchAppBuilderSettings,
    getAppBuilderSettingsValidationIssues,
    hasRequiredBuildSettings,
    IAppBuilderAppSettings,
    IAppBuilderSettingsValidationIssues,
    initializeAppBuilderSettings,
} from "./appBuilderAppDef";
import { IAppSizeEstimates } from "./AppSizeIndicator";

export interface IAppBuilderPublisherScreenState {
    prepareLog: IActionLogController;
    buildLog: IActionLogController;
    installLog: IActionLogController;
    measureLog: IActionLogController;
    status: IAppBuilderStatus;
    buildIsNeeded: boolean;
    hasRequiredBuildSettings: boolean;
    settingsValidationIssues: IAppBuilderSettingsValidationIssues;
    busyAction?: AppBuilderAction;
    progressPercent: number;
    progressStageCode?: string;
    sizeEstimates: IAppSizeEstimates;
    runAction: (action: AppBuilderAction) => void;
    cancelAction: () => void;
    showApkInExplorerInShell: () => void;
    markConfigurationChanged: () => void;
}

// Owns the App Builder screen's API state, websocket progress, and per-action logs.
export function useAppBuilderPublisherScreen(
    isActive: boolean,
): IAppBuilderPublisherScreenState {
    const prepareLog = useActionLogController();
    const buildLog = useActionLogController();
    const installLog = useActionLogController();
    const measureLog = useActionLogController();
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
    const statusRetryTimeoutRef = React.useRef<number>();
    const latestStatusRequestIdRef = React.useRef(0);
    const isMountedRef = React.useRef(true);
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
            if (!isMountedRef.current) {
                return;
            }

            setRawSizeEstimates(result.data);
        });
    }

    async function refreshSettings(appDefPath?: string): Promise<void> {
        const nextSettings = await fetchAppBuilderSettings(appDefPath);
        if (!isMountedRef.current) {
            return;
        }

        setSettings(nextSettings);
    }

    async function fetchStatusAsync(): Promise<IAppBuilderStatus> {
        const response = await getWithPromise("publish/rab/status");
        if (!response) {
            throw new Error(
                "Bloom could not read the Reading App Builder status.",
            );
        }
        const rawStatus =
            typeof response?.data === "string"
                ? (JSON.parse(response.data) as unknown)
                : response?.data;

        return normalizeStatus(rawStatus as IAppBuilderStatusApi);
    }

    async function refreshStatus(
        initializeSettingsAfterPrepare: boolean = false,
    ): Promise<void> {
        const requestId = latestStatusRequestIdRef.current + 1;
        latestStatusRequestIdRef.current = requestId;

        try {
            const nextStatus = await fetchStatusAsync();
            if (
                requestId !== latestStatusRequestIdRef.current ||
                !isMountedRef.current
            ) {
                return;
            }

            if (statusRetryTimeoutRef.current !== undefined) {
                window.clearTimeout(statusRetryTimeoutRef.current);
                statusRetryTimeoutRef.current = undefined;
            }
            setStatus(nextStatus);
            // If the backend reports an action is running but we have no local busyAction
            // (e.g. after a remount mid-build), restore the action and its last-known progress
            // so the indicator appears immediately without waiting for a websocket event.
            if (nextStatus.activeAction) {
                const isRestoringFromBlank = !busyActionRef.current;
                setBusyAction(
                    (current) =>
                        current ??
                        (nextStatus.activeAction as AppBuilderAction),
                );
                if (isRestoringFromBlank) {
                    if (nextStatus.activeActionProgressStage) {
                        setProgressStageCode(
                            nextStatus.activeActionProgressStage,
                        );
                    }
                    setProgressPercent(
                        nextStatus.activeActionProgressPercent ?? 0,
                    );
                }
            } else if (busyActionRef.current) {
                // Server is idle but client still thinks an action is running —
                // recover by clearing the stale busy state (e.g. after a remount
                // that missed the actionComplete websocket event).
                setBusyAction(undefined);
                setProgressPercent(0);
                setProgressStageCode(undefined);
            }

            if (initializeSettingsAfterPrepare && nextStatus.appDefPath) {
                const initializedSettings = await initializeAppBuilderSettings(
                    nextStatus.appDefPath,
                );
                if (
                    requestId !== latestStatusRequestIdRef.current ||
                    !isMountedRef.current
                ) {
                    return;
                }

                setSettings(initializedSettings);

                const refreshedStatus = await fetchStatusAsync();
                if (
                    requestId !== latestStatusRequestIdRef.current ||
                    !isMountedRef.current
                ) {
                    return;
                }

                setStatus(refreshedStatus);
                return;
            }

            void refreshSettings(nextStatus.appDefPath);
        } catch {
            if (statusRetryTimeoutRef.current !== undefined) {
                window.clearTimeout(statusRetryTimeoutRef.current);
            }

            if (isActive && isMountedRef.current) {
                statusRetryTimeoutRef.current = window.setTimeout(() => {
                    void refreshStatus(initializeSettingsAfterPrepare);
                }, 1000);
            }
        }
    }

    // Track busyAction in a ref so async callbacks can read the latest value
    // without closing over a stale render's state.
    const busyActionRef = React.useRef(busyAction);
    busyActionRef.current = busyAction;

    // This effect is warranted because tab activation is an external UI lifecycle boundary.
    // We fetch status first so any active build, its current stage, and its progress are
    // shown immediately — without waiting for the next websocket event.
    React.useEffect(() => {
        if (!isActive) {
            return;
        }

        void (async () => {
            // Snapshot the current server state right away.
            let statusFetchFailed = false;
            try {
                const nextStatus = await fetchStatusAsync();
                if (!isMountedRef.current) {
                    return;
                }
                setStatus(nextStatus);
                const activeActionFromServer = nextStatus.activeAction;
                if (activeActionFromServer) {
                    // Only restore server-side progress when we weren't already tracking
                    // the action locally — if busyAction is already set, live websocket
                    // events have fresher values than the status snapshot might.
                    const wasAlreadyTracking = !!busyActionRef.current;
                    setBusyAction(
                        (current) => current ?? activeActionFromServer!,
                    );
                    if (!wasAlreadyTracking) {
                        if (nextStatus.activeActionProgressStage) {
                            setProgressStageCode(
                                nextStatus.activeActionProgressStage,
                            );
                        }
                        setProgressPercent(
                            nextStatus.activeActionProgressPercent ?? 0,
                        );
                    }
                }
                void refreshSettings(nextStatus.appDefPath);
            } catch {
                // Status fetch failed; refreshStatus() below has its own retry handling.
                statusFetchFailed = true;
            }

            if (!isMountedRef.current) {
                return;
            }
            // BloomPUBs made on earlier visits are kept as long as they match their
            // books, so the estimates can include actual sizes right away (BL-17062).
            refreshSizeEstimates();
            if (statusFetchFailed) {
                void refreshStatus();
            }
        })();
        // fetchStatusAsync, refreshSettings, refreshSizeEstimates, and refreshStatus
        // are intentionally omitted — this only reruns at the tab-activation boundary.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [isActive]);

    // This effect is warranted because returning from Reading App Builder restores browser focus,
    // and that is our signal to refresh from the appDef after external edits.
    React.useEffect(() => {
        const handleWindowFocus = () => {
            if (!isActive) {
                return;
            }

            void refreshStatus();
            refreshSizeEstimates();
        };

        window.addEventListener("focus", handleWindowFocus);
        return () => {
            window.removeEventListener("focus", handleWindowFocus);
        };
        // refreshStatus and refreshSizeEstimates are intentionally omitted so this only reruns when the external focus boundary changes.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [isActive]);

    // This effect is warranted because the retry timer is an external browser resource that must be cleared on unmount.
    React.useEffect(() => {
        return () => {
            isMountedRef.current = false;
            latestStatusRequestIdRef.current += 1;
            if (statusRetryTimeoutRef.current !== undefined) {
                window.clearTimeout(statusRetryTimeoutRef.current);
            }
        };
    }, []);

    function getActionLogController(
        action: AppBuilderAction,
    ): IActionLogController {
        switch (action) {
            case "prepare":
                return prepareLog;
            case "build":
                return buildLog;
            case "install":
                return installLog;
            case "measure":
                return measureLog;
        }
    }

    // The progress stage to show while waiting for the server's first progress report.
    function getInitialProgressStageCode(action: AppBuilderAction): string {
        switch (action) {
            case "prepare":
                return "checking-installer";
            case "build":
                return "preparing-build";
            case "install":
                return "checking-device";
            case "measure":
                return "exporting-bloompubs";
        }
    }

    async function handleActionCompleted(
        action: AppBuilderAction,
        initializeSettingsAfterPrepare: boolean,
    ): Promise<void> {
        if (!isMountedRef.current) {
            return;
        }

        // We know the action is done — clear busy state immediately rather than
        // delegating to refreshStatus's recovery else-branch (which is for remounts
        // that missed the completion event).
        setBusyAction(undefined);
        setProgressPercent(0);
        setProgressStageCode(undefined);
        if (action === "prepare" || action === "build") {
            setPendingBuildNeeded(false);
            refreshSizeEstimates();
        }
        if (action === "measure") {
            refreshSizeEstimates();
        }
        // Fetch updated server state: APK path, project existence, build signature,
        // and (for prepare) initialize settings from the new appDef.
        await refreshStatus(initializeSettingsAfterPrepare);
    }

    function runAction(action: AppBuilderAction): void {
        if (action === "build" && !hasRequiredBuildSettings(settings)) {
            return;
        }
        // UI guards (canRunBuild, canRunPrepare …) should prevent this, but bail out
        // here before touching any shared state as a safety net.
        if (busyAction) {
            return;
        }

        getActionLogController(action).clear();

        // Reset visible progress before starting a new backend operation so reused accordions never show stale state.
        setProgressPercent(0);
        setProgressStageCode(getInitialProgressStageCode(action));
        setBusyAction(action);
        // The endpoint returns immediately; actual completion arrives via the
        // "actionComplete" websocket event handled by the subscription below.
        // Pass a failure callback so that a server-side rejection (e.g. another action
        // already running) doesn't leave busyAction permanently set.
        post(`publish/rab/${action}`, undefined, () => {
            // Don't think this can ever happen, since the only immediate failure mode is
            // if we try to start an action while another is running, and the UI should
            // prevent that. If it does, clear everything so at least the user can try again.
            setBusyAction(undefined);
            setProgressPercent(0);
            setProgressStageCode(undefined);
        });
    }

    // Listen for background-task completion from the server.
    // useWebSocketListener keeps the callback reference current on every render
    // so handleActionCompleted always has a fresh closure.
    useSubscribeToWebSocketForStringMessage(
        kAppBuilderWebSocketContext,
        kAppBuilderActionCompleteEventId,
        (result) => {
            const separatorIndex = result.indexOf(":");
            if (separatorIndex < 0) {
                return;
            }
            const completedAction = result.substring(
                0,
                separatorIndex,
            ) as AppBuilderAction;
            // guard against possibility of receiving a completion event that is stale somehow.
            if (completedAction !== busyActionRef.current) {
                return;
            }
            const succeeded =
                result.substring(separatorIndex + 1) === "success";
            void handleActionCompleted(
                completedAction,
                completedAction === "prepare" && succeeded,
            );
        },
    );

    // The server checks pictures for transparency in the background and tells us when better
    // size estimates are ready (BL-17062). The event carries no message, so this must not use
    // useSubscribeToWebSocketForStringMessage, which ignores events without one.
    useSubscribeToWebSocketForEvent(
        kAppBuilderWebSocketContext,
        kAppBuilderSizeEstimatesChangedEventId,
        () => {
            if (isMountedRef.current) {
                refreshSizeEstimates();
            }
        },
    );

    function showApkInExplorerInShell(): void {
        if (!status.apkPath) {
            return;
        }

        void postJson("fileIO/showInFolder", { folderPath: status.apkPath });
    }

    function cancelAction(): void {
        post("publish/rab/cancel");
    }

    function markConfigurationChanged(): void {
        setPendingBuildNeeded(true);
        void refreshStatus();
    }

    return {
        prepareLog,
        buildLog,
        installLog,
        measureLog,
        status,
        buildIsNeeded: status.buildNeeded || pendingBuildNeeded,
        hasRequiredBuildSettings: hasRequiredBuildSettings(settings),
        settingsValidationIssues:
            getAppBuilderSettingsValidationIssues(settings),
        busyAction,
        progressPercent,
        progressStageCode,
        sizeEstimates,
        runAction,
        cancelAction,
        showApkInExplorerInShell,
        markConfigurationChanged,
    };
}
