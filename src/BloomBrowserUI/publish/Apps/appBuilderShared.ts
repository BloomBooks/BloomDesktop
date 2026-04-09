import {
    defaultAppSizeEstimates,
    IAppSizeEstimateBook,
    IAppSizeEstimates,
} from "./AppSizeIndicator";
import { getBloomApiPrefix } from "../../utils/bloomApi";

export interface IAppBuilderTrackedBook {
    bookId: string;
    folderPath: string;
    title: string;
}

export interface IAppBuilderTrackedBookApi {
    bookId?: string;
    folderPath?: string;
    title?: string;
    BookId?: string;
    FolderPath?: string;
    Title?: string;
}

export interface IAppBuilderAppSettings {
    appName: string;
    mainColor: string;
    packageName: string;
    iconPath: string;
    copyright: string;
}

export interface IAppBuilderAppSettingsApi {
    appName?: string;
    mainColor?: string;
    packageName?: string;
    iconPath?: string;
    copyright?: string;
    AppName?: string;
    MainColor?: string;
    PackageName?: string;
    IconPath?: string;
    Copyright?: string;
}

export interface IAppBuilderStatus {
    rabInstalled: boolean;
    projectExists: boolean;
    apkExists: boolean;
    buildNeeded: boolean;
    prepareSteps: IAppBuilderPrepareStepStatus[];
    appDefPath?: string;
    appName?: string;
    apkPath?: string;
    apkSizeBytes?: number;
    rabRoot?: string;
    trackedBookTitles?: string[];
    trackedBooks: IAppBuilderTrackedBook[];
}

export type AppBuilderPrepareStepId =
    | "installer-available"
    | "rab-installed"
    | "build-tools-installed"
    | "signing-key-ready"
    | "project-created";

export interface IAppBuilderPrepareStepStatus {
    id: AppBuilderPrepareStepId;
    complete: boolean;
}

export interface IAppBuilderPrepareStepStatusApi {
    id?: AppBuilderPrepareStepId;
    complete?: boolean;
    Id?: AppBuilderPrepareStepId;
    Complete?: boolean;
}

export interface IAppBuilderStatusApi {
    rabInstalled?: boolean;
    projectExists?: boolean;
    apkExists?: boolean;
    buildNeeded?: boolean;
    prepareSteps?: IAppBuilderPrepareStepStatusApi[];
    appDefPath?: string;
    appName?: string;
    apkPath?: string;
    apkSizeBytes?: number;
    rabRoot?: string;
    trackedBookTitles?: string[];
    RabInstalled?: boolean;
    ProjectExists?: boolean;
    ApkExists?: boolean;
    BuildNeeded?: boolean;
    PrepareSteps?: IAppBuilderPrepareStepStatusApi[];
    AppDefPath?: string;
    AppName?: string;
    ApkPath?: string;
    ApkSizeBytes?: number;
    RabRoot?: string;
    TrackedBookTitles?: string[];
    trackedBooks?: IAppBuilderTrackedBookApi[];
    TrackedBooks?: IAppBuilderTrackedBookApi[];
}

export interface IAppBuilderBookSizeEstimateApi {
    bookId?: string;
    folderPath?: string;
    title?: string;
    sizeBytes?: number;
    isActual?: boolean;
    BookId?: string;
    FolderPath?: string;
    Title?: string;
    SizeBytes?: number;
    IsActual?: boolean;
}

export interface IAppBuilderSizeEstimatesApi {
    books?: IAppBuilderBookSizeEstimateApi[];
    estimatedAppOverheadBytes?: number;
    maxAppSizeBytes?: number;
    Books?: IAppBuilderBookSizeEstimateApi[];
    EstimatedAppOverheadBytes?: number;
    MaxAppSizeBytes?: number;
}

export interface IProgressStageLabels {
    preparingWorkspace: string;
    preparingBuild: string;
    checkingDevice: string;
    exportingBloompubs: string;
    generatingSigningKey: string;
    creatingProject: string;
    updatingProject: string;
    buildingAndroidApp: string;
    finalizingApk: string;
    installingOnPhone: string;
    launchingOnPhone: string;
    complete: string;
}

export const kAppBuilderWebSocketContext = "publish-rab";

export type AppBuilderAction = "setup" | "build" | "install";

export const defaultSettings: IAppBuilderAppSettings = {
    appName: "",
    mainColor: "#3F51B5",
    packageName: "",
    iconPath: "",
    copyright: "",
};

export const defaultStatus: IAppBuilderStatus = {
    rabInstalled: false,
    projectExists: false,
    apkExists: false,
    buildNeeded: false,
    prepareSteps: getDefaultPrepareSteps(),
    trackedBookTitles: [],
    trackedBooks: [],
};

export function getDefaultPrepareSteps(): IAppBuilderPrepareStepStatus[] {
    return [
        { id: "installer-available", complete: false },
        { id: "rab-installed", complete: false },
        { id: "build-tools-installed", complete: false },
        { id: "signing-key-ready", complete: false },
        { id: "project-created", complete: false },
    ];
}

// C# endpoints and tests can surface either PascalCase or camelCase, so normalize once at the UI boundary.
export function normalizeTrackedBook(
    trackedBook?: IAppBuilderTrackedBookApi,
): IAppBuilderTrackedBook {
    return {
        bookId: trackedBook?.bookId ?? trackedBook?.BookId ?? "",
        folderPath: trackedBook?.folderPath ?? trackedBook?.FolderPath ?? "",
        title: trackedBook?.title ?? trackedBook?.Title ?? "",
    };
}

export function normalizeSettings(
    settings?: IAppBuilderAppSettingsApi,
): IAppBuilderAppSettings {
    return {
        appName: settings?.appName ?? settings?.AppName ?? "",
        mainColor:
            settings?.mainColor ??
            settings?.MainColor ??
            defaultSettings.mainColor,
        packageName: settings?.packageName ?? settings?.PackageName ?? "",
        iconPath: settings?.iconPath ?? settings?.IconPath ?? "",
        copyright: settings?.copyright ?? settings?.Copyright ?? "",
    };
}

export function normalizePrepareStepStatus(
    step?: IAppBuilderPrepareStepStatusApi,
): IAppBuilderPrepareStepStatus {
    const stepId = step?.id ?? step?.Id;
    const fallback = getDefaultPrepareSteps()[0];

    return {
        id: stepId ?? fallback.id,
        complete: step?.complete ?? step?.Complete ?? false,
    };
}

export function normalizeBookSizeEstimate(
    estimate?: IAppBuilderBookSizeEstimateApi,
): IAppSizeEstimateBook {
    return {
        bookId: estimate?.bookId ?? estimate?.BookId ?? "",
        folderPath: estimate?.folderPath ?? estimate?.FolderPath ?? "",
        title: estimate?.title ?? estimate?.Title ?? "",
        sizeBytes: estimate?.sizeBytes ?? estimate?.SizeBytes ?? 0,
        isActual: estimate?.isActual ?? estimate?.IsActual ?? false,
    };
}

export function getDefaultAppBuilderIconUrl(): string {
    return `${getBloomApiPrefix(false)}branding/shared/bloom-icon.png`;
}

export function getBloomLocalFileUrl(filePath: string): string {
    const normalizedPath = filePath.replace(/\\/g, "/");
    const encodedPath = normalizedPath
        .split("/")
        .map((segment) => encodeURIComponent(segment))
        .join("/");

    return `${getBloomApiPrefix(false)}${encodedPath}`;
}

export function normalizeStatus(
    status?: IAppBuilderStatusApi,
): IAppBuilderStatus {
    const normalizedPrepareSteps = (
        status?.prepareSteps ??
        status?.PrepareSteps ??
        getDefaultPrepareSteps()
    ).map(normalizePrepareStepStatus);

    return {
        rabInstalled: status?.rabInstalled ?? status?.RabInstalled ?? false,
        projectExists: status?.projectExists ?? status?.ProjectExists ?? false,
        apkExists: status?.apkExists ?? status?.ApkExists ?? false,
        buildNeeded: status?.buildNeeded ?? status?.BuildNeeded ?? false,
        prepareSteps:
            normalizedPrepareSteps.length > 0
                ? normalizedPrepareSteps
                : getDefaultPrepareSteps(),
        appDefPath: status?.appDefPath ?? status?.AppDefPath,
        appName: status?.appName ?? status?.AppName,
        apkPath: status?.apkPath ?? status?.ApkPath,
        apkSizeBytes: status?.apkSizeBytes ?? status?.ApkSizeBytes ?? 0,
        rabRoot: status?.rabRoot ?? status?.RabRoot,
        trackedBookTitles:
            status?.trackedBookTitles ?? status?.TrackedBookTitles ?? [],
        trackedBooks: (status?.trackedBooks ?? status?.TrackedBooks ?? []).map(
            normalizeTrackedBook,
        ),
    };
}

export function normalizeSizeEstimates(
    sizeEstimates?: IAppBuilderSizeEstimatesApi,
): IAppSizeEstimates {
    return {
        books: (sizeEstimates?.books ?? sizeEstimates?.Books ?? []).map(
            normalizeBookSizeEstimate,
        ),
        estimatedAppOverheadBytes:
            sizeEstimates?.estimatedAppOverheadBytes ??
            sizeEstimates?.EstimatedAppOverheadBytes ??
            defaultAppSizeEstimates.estimatedAppOverheadBytes,
        maxAppSizeBytes:
            sizeEstimates?.maxAppSizeBytes ??
            sizeEstimates?.MaxAppSizeBytes ??
            defaultAppSizeEstimates.maxAppSizeBytes,
    };
}

export function getProgressStageLabel(
    busyAction: AppBuilderAction | undefined,
    stageCode: string | undefined,
    labels: IProgressStageLabels,
): string | undefined {
    if (
        busyAction !== "setup" &&
        busyAction !== "build" &&
        busyAction !== "install"
    ) {
        return undefined;
    }

    switch (stageCode) {
        case "preparing-workspace":
            return labels.preparingWorkspace;
        case "preparing-build":
            return labels.preparingBuild;
        case "checking-device":
            return labels.checkingDevice;
        case "exporting-bloompubs":
            return labels.exportingBloompubs;
        case "generating-signing-key":
            return labels.generatingSigningKey;
        case "creating-project":
            return labels.creatingProject;
        case "updating-project":
            return labels.updatingProject;
        case "building-android-app":
            return labels.buildingAndroidApp;
        case "finalizing-apk":
            return labels.finalizingApk;
        case "installing-on-phone":
            return labels.installingOnPhone;
        case "launching-on-phone":
            return labels.launchingOnPhone;
        case "complete":
            return labels.complete;
        default:
            if (busyAction === "setup") {
                return labels.preparingWorkspace;
            }

            if (busyAction === "build") {
                return labels.preparingBuild;
            }

            return labels.checkingDevice;
    }
}

export function getProgressStageCodeFromMessage(
    message?: string,
): string | undefined {
    // Fallback for operations that still emit plain progress text before or instead of an explicit stage event.
    if (!message) {
        return undefined;
    }

    if (message.includes("Preparing the Reading App Builder workspace")) {
        return "preparing-workspace";
    }

    if (message.includes("Creating BloomPUB for")) {
        return "exporting-bloompubs";
    }

    if (message.includes("Checking for a connected Android device")) {
        return "checking-device";
    }

    if (message.includes("Generating a signing key")) {
        return "generating-signing-key";
    }

    if (message.includes("Creating the initial Reading App Builder project")) {
        return "creating-project";
    }

    if (
        message.includes("Refreshing BloomPUB inputs") ||
        message.includes(
            "Updating the Reading App Builder project with fresh BloomPUB files",
        )
    ) {
        return "updating-project";
    }

    if (message.includes("Building the Android app with Reading App Builder")) {
        return "building-android-app";
    }

    if (message.startsWith("APK:")) {
        return "finalizing-apk";
    }

    if (message.includes("Installing ") && message.includes(" on ")) {
        return "installing-on-phone";
    }

    if (message.includes("Launching ") && message.includes(" on ")) {
        return "launching-on-phone";
    }

    if (
        message.includes("Setup complete.") ||
        message.includes("Build complete.") ||
        message.includes("Install complete.")
    ) {
        return "complete";
    }

    return undefined;
}

export function getPrepareStepIdForStage(
    busyAction: AppBuilderAction | undefined,
    stageCode: string | undefined,
): AppBuilderPrepareStepId | undefined {
    if (busyAction !== "setup" || !stageCode) {
        return undefined;
    }

    switch (stageCode) {
        case "checking-installer":
        case "downloading-installer":
            return "installer-available";
        case "running-installer":
            return "rab-installed";
        case "installing-build-tools":
            return "build-tools-installed";
        case "generating-signing-key":
            return "signing-key-ready";
        case "preparing-workspace":
        case "exporting-bloompubs":
        case "creating-project":
        case "updating-project":
        case "complete":
            return "project-created";
        default:
            return undefined;
    }
}

export function arePrepareStepsComplete(
    prepareSteps: IAppBuilderPrepareStepStatus[],
): boolean {
    return prepareSteps.every((step) => step.complete);
}

export function normalizeConfigrSettings(
    settingsValue: string | object | undefined,
): object | undefined {
    // Configr sometimes returns a parsed object and sometimes a serialized JSON payload.
    if (!settingsValue) {
        return undefined;
    }

    if (typeof settingsValue === "string") {
        return JSON.parse(settingsValue) as object;
    }

    return settingsValue;
}
