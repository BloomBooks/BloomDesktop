import * as React from "react";
import {
    getWithPromise,
    postJsonAsync,
    useApiObject,
} from "../../utils/bloomApi";

export interface IAppBuilderAppSettings {
    appName: string;
    colorScheme: string;
    packageName: string;
    iconPath: string;
    copyright: string;
    about: string;
}

export interface IAppBuilderAppSettingsApi {
    appName?: string;
    colorScheme?: string;
    packageName?: string;
    iconPath?: string;
    copyright?: string;
    about?: string;
    AppName?: string;
    ColorScheme?: string;
    PackageName?: string;
    IconPath?: string;
    Copyright?: string;
    About?: string;
}

export interface IAppBuilderIconChoiceApi {
    id?: string;
    label?: string;
    iconPath?: string;
    Id?: string;
    Label?: string;
    IconPath?: string;
}

export interface IAppBuilderIconChoice {
    id: string;
    label: string;
    iconPath: string;
}

export interface IAppBuilderSettingsValidationIssues {
    appName?: "required";
    packageName?: "required" | "invalid";
    copyright?: "required";
    about?: "required";
}

export const kDefaultAppBuilderIconId = "bloom-app-icon-52";

export const defaultSettings: IAppBuilderAppSettings = {
    appName: "",
    colorScheme: "Indigo",
    packageName: "",
    iconPath: "",
    copyright: "",
    about: "",
};

const kAppBuilderPackageNamePattern = /^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$/;
const kAdaptiveForegroundFileName = "ic_launcher_foreground.png";
const kAdaptiveForegroundRelativePath = `mipmap-xxxhdpi\\${kAdaptiveForegroundFileName}`;
const kLauncherIconDefinitions = [
    {
        width: "36",
        height: "36",
        relativePath: "drawable-ldpi\\ic_launcher.png",
    },
    {
        width: "48",
        height: "48",
        relativePath: "drawable-mdpi\\ic_launcher.png",
    },
    {
        width: "72",
        height: "72",
        relativePath: "drawable-hdpi\\ic_launcher.png",
    },
    {
        width: "96",
        height: "96",
        relativePath: "drawable-xhdpi\\ic_launcher.png",
    },
    {
        width: "144",
        height: "144",
        relativePath: "drawable-xxhdpi\\ic_launcher.png",
    },
    {
        width: "192",
        height: "192",
        relativePath: "drawable-xxxhdpi\\ic_launcher.png",
    },
    {
        width: "512",
        height: "512",
        relativePath: "drawable-web\\ic_launcher.png",
    },
];

function firstNonEmpty(...values: Array<string | undefined>): string {
    return values.find((value) => !!value && value.trim().length > 0) ?? "";
}

function normalizeApiSettings(
    settings?: IAppBuilderAppSettingsApi,
): IAppBuilderAppSettings {
    return {
        appName: firstNonEmpty(settings?.appName, settings?.AppName),
        colorScheme: firstNonEmpty(
            settings?.colorScheme,
            settings?.ColorScheme,
            defaultSettings.colorScheme,
        ),
        packageName: firstNonEmpty(
            settings?.packageName,
            settings?.PackageName,
        ),
        iconPath: firstNonEmpty(settings?.iconPath, settings?.IconPath),
        copyright: firstNonEmpty(settings?.copyright, settings?.Copyright),
        about: firstNonEmpty(settings?.about, settings?.About),
    };
}

function mergeSettings(
    preferredSettings: IAppBuilderAppSettings,
    fallbackSettings: IAppBuilderAppSettings,
): IAppBuilderAppSettings {
    return {
        appName: firstNonEmpty(
            preferredSettings.appName,
            fallbackSettings.appName,
        ),
        colorScheme: firstNonEmpty(
            preferredSettings.colorScheme,
            fallbackSettings.colorScheme,
            defaultSettings.colorScheme,
        ),
        packageName: firstNonEmpty(
            preferredSettings.packageName,
            fallbackSettings.packageName,
        ),
        iconPath: firstNonEmpty(
            preferredSettings.iconPath,
            fallbackSettings.iconPath,
        ),
        copyright: firstNonEmpty(
            preferredSettings.copyright,
            fallbackSettings.copyright,
        ),
        about: firstNonEmpty(preferredSettings.about, fallbackSettings.about),
    };
}

function settingsAreEqual(
    left: IAppBuilderAppSettings,
    right: IAppBuilderAppSettings,
): boolean {
    return (
        left.appName === right.appName &&
        left.colorScheme === right.colorScheme &&
        left.packageName === right.packageName &&
        left.iconPath === right.iconPath &&
        left.copyright === right.copyright &&
        left.about === right.about
    );
}

export function getAppBuilderSettingsFromAppDef(
    appDefPath: string,
    appDefContents: string,
): IAppBuilderAppSettings {
    const appDefDocument = parseAppDefDocument(appDefContents);
    return {
        appName: getDefaultAppName(appDefDocument) ?? "",
        colorScheme:
            getColorSchemeName(appDefDocument) ?? defaultSettings.colorScheme,
        packageName: getSingleElementText(appDefDocument, "package") ?? "",
        iconPath: getConfiguredIconPath(appDefPath, appDefDocument) ?? "",
        copyright: getMetadataContent(appDefDocument, "copyright-text") ?? "",
        about: defaultSettings.about,
    };
}

export function updateAppBuilderAppDef(
    appDefContents: string,
    settings: IAppBuilderAppSettings,
): string {
    const appDefDocument = parseAppDefDocument(appDefContents);
    const root = appDefDocument.documentElement;

    setDefaultAppName(root, settings.appName.trim());
    setApkFileName(root, settings.appName.trim());
    setSingleElementText(root, "package", settings.packageName.trim());
    setColorSchemeName(root, settings.colorScheme.trim());
    setMetadataContent(root, "copyright-text", settings.copyright.trim());
    setAboutFileName(root);
    setLauncherImagePaths(root);
    setAdaptiveForegroundImage(root);

    return serializeAppDefDocument(appDefDocument);
}

export function hasRequiredBuildSettings(
    settings: IAppBuilderAppSettings,
): boolean {
    const validationIssues = getAppBuilderSettingsValidationIssues(settings);
    return !Object.values(validationIssues).some((issue) => !!issue);
}

export function getAppBuilderSettingsValidationIssues(
    settings: IAppBuilderAppSettings,
): IAppBuilderSettingsValidationIssues {
    return {
        appName: settings.appName.trim() ? undefined : "required",
        packageName: getAppBuilderPackageNameValidationIssue(
            settings.packageName,
        ),
        copyright: settings.copyright.trim() ? undefined : "required",
        about: settings.about.trim() ? undefined : "required",
    };
}

export function getAppBuilderPackageNameValidationIssue(
    packageName: string,
): "required" | "invalid" | undefined {
    const trimmedPackageName = packageName.trim();
    if (!trimmedPackageName) {
        return "required";
    }

    return kAppBuilderPackageNamePattern.test(trimmedPackageName)
        ? undefined
        : "invalid";
}

export function normalizeIconChoice(
    choice?: IAppBuilderIconChoiceApi,
): IAppBuilderIconChoice {
    return {
        id: choice?.id ?? choice?.Id ?? "",
        label: choice?.label ?? choice?.Label ?? "",
        iconPath: choice?.iconPath ?? choice?.IconPath ?? "",
    };
}

export function getDefaultAppBuilderIconChoice(
    choices: IAppBuilderIconChoice[],
): IAppBuilderIconChoice | undefined {
    return (
        choices.find((choice) => choice.id === kDefaultAppBuilderIconId) ??
        choices[0]
    );
}

export function applyDefaultAppBuilderIconChoice(
    settings: IAppBuilderAppSettings,
    choices: IAppBuilderIconChoice[],
): IAppBuilderAppSettings {
    if (settings.iconPath) {
        return settings;
    }

    const defaultChoice = getDefaultAppBuilderIconChoice(choices);
    if (!defaultChoice?.iconPath) {
        return settings;
    }

    return {
        ...settings,
        iconPath: defaultChoice.iconPath,
    };
}

export function useAppBuilderAppSettings(): IAppBuilderAppSettings {
    return defaultSettings;
}

export function useAppBuilderAppSettingsForPath(
    appDefPath?: string,
): IAppBuilderAppSettings {
    const [settings, setSettings] = React.useState(defaultSettings);

    // This effect is warranted because appDef loading is asynchronous and must refresh when the current project file changes.
    React.useEffect(() => {
        let isCancelled = false;

        void fetchAppBuilderSettings(appDefPath).then((nextSettings) => {
            if (!isCancelled) {
                setSettings(nextSettings);
            }
        });

        return () => {
            isCancelled = true;
        };
    }, [appDefPath]);

    return settings;
}

export function useAppBuilderIconChoices(): IAppBuilderIconChoice[] {
    const rawChoices = useApiObject<IAppBuilderIconChoiceApi[]>(
        "publish/rab/icon-choices",
        [],
    );

    return React.useMemo(
        () => rawChoices.map(normalizeIconChoice),
        [rawChoices],
    );
}

export async function fetchAppBuilderSettings(
    appDefPath?: string,
): Promise<IAppBuilderAppSettings> {
    if (!appDefPath) {
        return defaultSettings;
    }

    const response = await getWithPromise("publish/rab/settings");
    const rawSettings =
        typeof response?.data === "string"
            ? (JSON.parse(response.data) as unknown)
            : response?.data;

    return normalizeApiSettings(rawSettings as IAppBuilderAppSettingsApi);
}

export async function fetchDefaultAppBuilderSettings(): Promise<IAppBuilderAppSettings> {
    const response = await getWithPromise("publish/rab/default-settings");
    const rawSettings =
        typeof response?.data === "string"
            ? (JSON.parse(response.data) as unknown)
            : response?.data;

    return normalizeApiSettings(rawSettings as IAppBuilderAppSettingsApi);
}

export async function initializeAppBuilderSettings(
    appDefPath: string,
): Promise<IAppBuilderAppSettings> {
    const [rawSettings, defaultAppSettings] = await Promise.all([
        fetchAppBuilderSettings(appDefPath),
        fetchDefaultAppBuilderSettings(),
    ]);
    const initializedSettings = mergeSettings(rawSettings, defaultAppSettings);

    if (!settingsAreEqual(rawSettings, initializedSettings)) {
        await saveAppBuilderSettings(appDefPath, initializedSettings);
    }

    return initializedSettings;
}

export function refreshAppBuilderSettings(
    setSettings: React.Dispatch<React.SetStateAction<IAppBuilderAppSettings>>,
    appDefPath?: string,
): void {
    void fetchAppBuilderSettings(appDefPath).then((settings) => {
        setSettings(settings);
    });
}

export async function saveAppBuilderSettings(
    appDefPath: string,
    settings: IAppBuilderAppSettings,
): Promise<boolean> {
    await postJsonAsync("publish/rab/settings", settings);

    return true;
}

export async function fetchAppBuilderIconChoices(): Promise<
    IAppBuilderIconChoice[]
> {
    const response = await getWithPromise("publish/rab/icon-choices");
    const rawChoices =
        typeof response?.data === "string"
            ? (JSON.parse(response.data) as unknown)
            : response?.data;

    return Array.isArray(rawChoices)
        ? rawChoices.map((choice) =>
              normalizeIconChoice(choice as IAppBuilderIconChoiceApi),
          )
        : [];
}

function parseAppDefDocument(appDefContents: string): XMLDocument {
    const appDefDocument = new DOMParser().parseFromString(
        appDefContents,
        "application/xml",
    );
    if (appDefDocument.getElementsByTagName("parsererror").length > 0) {
        throw new Error(
            "Bloom could not parse the Reading App Builder app definition.",
        );
    }

    return appDefDocument;
}

function serializeAppDefDocument(appDefDocument: XMLDocument): string {
    const serialized = new XMLSerializer().serializeToString(appDefDocument);
    return serialized.startsWith("<?xml")
        ? serialized
        : `<?xml version="1.0" encoding="utf-8"?>\n${serialized}`;
}

function getDirectChild(parent: Element, tagName: string): Element | undefined {
    return Array.from(parent.children).find(
        (child) => child.tagName === tagName,
    );
}

function getDirectChildByAttribute(
    parent: Element,
    tagName: string,
    attributeName: string,
    attributeValue: string,
): Element | undefined {
    return Array.from(parent.children).find(
        (child) =>
            child.tagName === tagName &&
            child.getAttribute(attributeName) === attributeValue,
    );
}

function getSingleElementText(
    appDefDocument: XMLDocument,
    tagName: string,
): string | undefined {
    return appDefDocument.documentElement
        .getElementsByTagName(tagName)[0]
        ?.textContent?.trim();
}

function getDefaultAppName(appDefDocument: XMLDocument): string | undefined {
    return Array.from(
        appDefDocument.documentElement.getElementsByTagName("app-name"),
    )
        .find((element) => element.getAttribute("lang") === "default")
        ?.textContent?.trim();
}

function getColorSchemeName(appDefDocument: XMLDocument): string | undefined {
    return appDefDocument.documentElement
        .getElementsByTagName("color-scheme")[0]
        ?.getAttribute("name")
        ?.trim();
}

function getMetadataContent(
    appDefDocument: XMLDocument,
    metadataName: string,
): string | undefined {
    return Array.from(
        appDefDocument.documentElement.getElementsByTagName("meta"),
    )
        .find((element) => element.getAttribute("name") === metadataName)
        ?.getAttribute("content")
        ?.trim();
}

function getConfiguredIconPath(
    appDefPath: string,
    appDefDocument: XMLDocument,
): string | undefined {
    const adaptiveForegroundFileName = appDefDocument.documentElement
        .getElementsByTagName("adaptive-icon")[0]
        ?.getElementsByTagName("foreground")[0]
        ?.getElementsByTagName("image")[0]
        ?.textContent?.trim();
    if (adaptiveForegroundFileName) {
        return joinWindowsPath(
            getProjectImagesRoot(appDefPath),
            "mipmap-xxxhdpi",
            adaptiveForegroundFileName,
        );
    }

    const launcherImagesElement = Array.from(
        appDefDocument.documentElement.getElementsByTagName("images"),
    ).find((element) => element.getAttribute("type") === "launcher");
    const launcherRelativePath = Array.from(
        launcherImagesElement?.children ?? [],
    )
        .find((element) => element.tagName === "image")
        ?.textContent?.trim();
    if (!launcherRelativePath) {
        return undefined;
    }

    return joinWindowsPath(
        getProjectImagesRoot(appDefPath),
        launcherRelativePath,
    );
}

function setDefaultAppName(root: Element, appName: string): void {
    let appNameElement = getDirectChildByAttribute(
        root,
        "app-name",
        "lang",
        "default",
    );
    if (!appNameElement) {
        appNameElement = root.ownerDocument.createElement("app-name");
        appNameElement.setAttribute("lang", "default");
        root.appendChild(appNameElement);
    }

    appNameElement.textContent = appName;
}

function setApkFileName(root: Element, appName: string): void {
    let apkFileNameElement = getDirectChild(root, "apk-filename");
    if (!apkFileNameElement) {
        apkFileNameElement = root.ownerDocument.createElement("apk-filename");
        root.appendChild(apkFileNameElement);
    }

    apkFileNameElement.setAttribute("append-version", "true");
    apkFileNameElement.textContent = getApkFileName(appName);
}

function setSingleElementText(
    root: Element,
    tagName: string,
    value: string,
): void {
    let element = getDirectChild(root, tagName);
    if (!element) {
        element = root.ownerDocument.createElement(tagName);
        root.appendChild(element);
    }

    element.textContent = value;
}

function setColorSchemeName(root: Element, colorScheme: string): void {
    let colorSchemeElement = getDirectChild(root, "color-scheme");
    if (!colorSchemeElement) {
        colorSchemeElement = root.ownerDocument.createElement("color-scheme");
        root.appendChild(colorSchemeElement);
    }

    colorSchemeElement.setAttribute("name", colorScheme);
}

function setMetadataContent(
    root: Element,
    metadataName: string,
    content: string,
): void {
    let booksElement = getDirectChild(root, "books");
    if (!booksElement) {
        booksElement = root.ownerDocument.createElement("books");
        booksElement.setAttribute("id", "C01");
        root.appendChild(booksElement);
    }

    if (!booksElement.getAttribute("id")) {
        booksElement.setAttribute("id", "C01");
    }

    let metadataElement = getDirectChild(booksElement, "metadata");
    if (!metadataElement) {
        metadataElement = root.ownerDocument.createElement("metadata");
        booksElement.appendChild(metadataElement);
    }

    let metaElement = Array.from(metadataElement.children).find(
        (element) =>
            element.tagName === "meta" &&
            element.getAttribute("name") === metadataName,
    );
    if (!content) {
        metaElement?.remove();
        return;
    }

    if (!metaElement) {
        metaElement = root.ownerDocument.createElement("meta");
        metaElement.setAttribute("name", metadataName);
        metadataElement.appendChild(metaElement);
    }

    metaElement.setAttribute("content", content);
}

function setLauncherImagePaths(root: Element): void {
    let imagesElement = Array.from(root.children).find(
        (element) =>
            element.tagName === "images" &&
            element.getAttribute("type") === "launcher",
    );
    if (!imagesElement) {
        imagesElement = root.ownerDocument.createElement("images");
        imagesElement.setAttribute("type", "launcher");
        root.appendChild(imagesElement);
    }

    while (imagesElement.firstChild) {
        imagesElement.removeChild(imagesElement.firstChild);
    }

    kLauncherIconDefinitions.forEach((iconDefinition) => {
        const imageElement = root.ownerDocument.createElement("image");
        imageElement.setAttribute("width", iconDefinition.width);
        imageElement.setAttribute("height", iconDefinition.height);
        imageElement.textContent = iconDefinition.relativePath;
        imagesElement.appendChild(imageElement);
    });
}

function setAdaptiveForegroundImage(root: Element): void {
    let adaptiveIconElement = getDirectChild(root, "adaptive-icon");
    if (!adaptiveIconElement) {
        adaptiveIconElement = root.ownerDocument.createElement("adaptive-icon");
        root.appendChild(adaptiveIconElement);
    }

    let foregroundElement = getDirectChild(adaptiveIconElement, "foreground");
    if (!foregroundElement) {
        foregroundElement = root.ownerDocument.createElement("foreground");
        adaptiveIconElement.appendChild(foregroundElement);
    }

    let imageElement = getDirectChild(foregroundElement, "image");
    if (!imageElement) {
        imageElement = root.ownerDocument.createElement("image");
        foregroundElement.appendChild(imageElement);
    }

    imageElement.textContent = kAdaptiveForegroundFileName;
}

function setAboutFileName(root: Element): void {
    let aboutElement = getDirectChild(root, "about");
    if (!aboutElement) {
        aboutElement = root.ownerDocument.createElement("about");
        root.appendChild(aboutElement);
    }

    aboutElement.setAttribute("enabled", "true");

    let fileNameElement = getDirectChild(aboutElement, "filename");
    if (!fileNameElement) {
        fileNameElement = root.ownerDocument.createElement("filename");
        aboutElement.appendChild(fileNameElement);
    }

    fileNameElement.textContent = "about.txt";
}

function getApkFileName(appName: string): string {
    const safeName = Array.from(appName)
        .map((character) => (/[A-Za-z0-9]/.test(character) ? character : "_"))
        .join("")
        .replace(/^_+|_+$/g, "");

    return `${safeName || "Bloom_App"}.apk`;
}

async function syncAppBuilderIconFiles(
    appDefPath: string,
    iconSourcePath: string,
): Promise<void> {
    if (!iconSourcePath) {
        return;
    }

    const destinationPaths = [
        joinWindowsPath(
            getProjectImagesRoot(appDefPath),
            kAdaptiveForegroundRelativePath,
        ),
        ...kLauncherIconDefinitions.map((iconDefinition) =>
            joinWindowsPath(
                getProjectImagesRoot(appDefPath),
                iconDefinition.relativePath,
            ),
        ),
    ];

    await Promise.all(
        destinationPaths.map(async (destinationPath) => {
            if (
                normalizePathForComparison(iconSourcePath) ===
                normalizePathForComparison(destinationPath)
            ) {
                return;
            }

            await postJsonAsync("fileIO/copyFile", {
                from: encodeURIComponent(iconSourcePath),
                to: encodeURIComponent(destinationPath),
            });
        }),
    );
}

function getProjectImagesRoot(appDefPath: string): string {
    return joinWindowsPath(getProjectDataFolder(appDefPath), "images");
}

function getProjectDataFolder(appDefPath: string): string {
    return joinWindowsPath(
        getDirectoryName(appDefPath),
        `${getFileNameWithoutExtension(appDefPath)}_data`,
    );
}

export function getAboutTextPath(appDefPath: string): string {
    return joinWindowsPath(
        getBloomAppDataRoot(appDefPath),
        "project-assets",
        "about.txt",
    );
}

function getBloomAppDataRoot(appDefPath: string): string {
    const normalizedPath = appDefPath.replace(/[\\/]+/g, "\\");
    const marker = "\\Bloom App Data\\";
    const markerIndex = normalizedPath
        .toLowerCase()
        .lastIndexOf(marker.toLowerCase());
    if (markerIndex >= 0) {
        return normalizedPath.substring(0, markerIndex + marker.length - 1);
    }

    return getDirectoryName(appDefPath);
}

function getDirectoryName(path: string): string {
    const normalizedPath = path.replace(/[\\/]+/g, "\\");
    const lastSeparatorIndex = normalizedPath.lastIndexOf("\\");
    return lastSeparatorIndex >= 0
        ? normalizedPath.substring(0, lastSeparatorIndex)
        : "";
}

function getFileNameWithoutExtension(path: string): string {
    const normalizedPath = path.replace(/[\\/]+/g, "\\");
    const fileName = normalizedPath.substring(
        normalizedPath.lastIndexOf("\\") + 1,
    );
    const lastDotIndex = fileName.lastIndexOf(".");
    return lastDotIndex > 0 ? fileName.substring(0, lastDotIndex) : fileName;
}

function joinWindowsPath(...segments: string[]): string {
    return segments
        .filter((segment) => !!segment)
        .map((segment, index) => {
            if (index === 0) {
                return segment.replace(/[\\/]+$/g, "");
            }

            return segment.replace(/^[\\/]+|[\\/]+$/g, "");
        })
        .join("\\");
}

function normalizePathForComparison(path: string): string {
    return path.replace(/[\\/]+/g, "\\").toLowerCase();
}

async function fetchAppBuilderAboutText(appDefPath: string): Promise<string> {
    try {
        const response = await postJsonAsync("fileIO/readFile", {
            path: getAboutTextPath(appDefPath),
        });
        return typeof response?.data === "string" ? response.data : "";
    } catch {
        return "";
    }
}
