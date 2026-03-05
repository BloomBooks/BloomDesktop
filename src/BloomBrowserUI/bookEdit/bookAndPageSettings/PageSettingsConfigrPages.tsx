import * as React from "react";
import {
    ConfigrCustomStringInput,
    ConfigrGroup,
    ConfigrPage,
} from "@sillsdev/config-r";
import tinycolor from "tinycolor2";
import {
    ColorDisplayButton,
    DialogResult,
} from "../../react_components/color-picking/colorPickerDialog";
import { BloomPalette } from "../../react_components/color-picking/bloomPalette";
import { useL10n } from "../../react_components/l10nHooks";
import { getPageIframeBody } from "../../utils/shared";

export type IPageSettings = {
    page: {
        backgroundColor: string;
        pageNumberColor: string;
        pageNumberBackgroundColor: string;
    };
};

export const getCurrentPageElement = (): HTMLElement => {
    const page = getPageIframeBody()?.querySelector(
        ".bloom-page",
    ) as HTMLElement | null;
    if (!page) {
        throw new Error(
            "PageSettingsConfigrPages could not find .bloom-page in the page iframe",
        );
    }
    return page;
};

const normalizeToHexOrEmpty = (color: string): string => {
    const trimmed = color.trim();
    if (!trimmed) {
        return "";
    }

    const parsed = tinycolor(trimmed);
    if (!parsed.isValid()) {
        return trimmed;
    }

    // Treat fully transparent as "not set".
    if (parsed.getAlpha() === 0) {
        return "";
    }

    if (parsed.getAlpha() < 1) {
        return parsed.toHex8String().toUpperCase();
    }

    return parsed.toHexString().toUpperCase();
};

const getComputedStyleForPage = (page: HTMLElement): CSSStyleDeclaration => {
    const view = page.ownerDocument.defaultView;
    if (view) {
        return view.getComputedStyle(page);
    }
    return getComputedStyle(page);
};

const getCurrentPageBackgroundColor = (): string => {
    const page = getCurrentPageElement();
    const computedPage = getComputedStyleForPage(page);

    const inlineMarginBox = normalizeToHexOrEmpty(
        page.style.getPropertyValue("--marginBox-background-color"),
    );
    if (inlineMarginBox) return inlineMarginBox;

    const inline = normalizeToHexOrEmpty(
        page.style.getPropertyValue("--page-background-color"),
    );
    if (inline) return inline;

    const computedMarginBoxVariable = normalizeToHexOrEmpty(
        computedPage.getPropertyValue("--marginBox-background-color"),
    );
    if (computedMarginBoxVariable) return computedMarginBoxVariable;

    const computedVariable = normalizeToHexOrEmpty(
        computedPage.getPropertyValue("--page-background-color"),
    );
    if (computedVariable) return computedVariable;

    const marginBox = page.querySelector(".marginBox") as HTMLElement | null;
    if (marginBox) {
        const computedMarginBoxBackground = normalizeToHexOrEmpty(
            getComputedStyleForPage(marginBox).backgroundColor,
        );
        if (computedMarginBoxBackground) return computedMarginBoxBackground;
    }

    const computedBackground = normalizeToHexOrEmpty(
        computedPage.backgroundColor,
    );
    return computedBackground || "#FFFFFF";
};

const setOrRemoveCustomProperty = (
    style: CSSStyleDeclaration,
    propertyName: string,
    value: string,
): void => {
    const normalized = normalizeToHexOrEmpty(value);
    if (normalized) {
        style.setProperty(propertyName, normalized);
    } else {
        style.removeProperty(propertyName);
    }
};

const setCurrentPageBackgroundColor = (color: string): void => {
    const page = getCurrentPageElement();
    setOrRemoveCustomProperty(page.style, "--page-background-color", color);
    setOrRemoveCustomProperty(
        page.style,
        "--marginBox-background-color",
        color,
    );
};

const getPageNumberColor = (): string => {
    const page = getCurrentPageElement();

    const inline = normalizeToHexOrEmpty(
        page.style.getPropertyValue("--pageNumber-color"),
    );
    if (inline) return inline;

    const computed = normalizeToHexOrEmpty(
        getComputedStyleForPage(page).getPropertyValue("--pageNumber-color"),
    );
    return computed || "#000000";
};

const setPageNumberColor = (color: string): void => {
    const page = getCurrentPageElement();
    setOrRemoveCustomProperty(page.style, "--pageNumber-color", color);
};

const getPageNumberBackgroundColor = (): string => {
    const page = getCurrentPageElement();

    const inline = normalizeToHexOrEmpty(
        page.style.getPropertyValue("--pageNumber-background-color"),
    );
    if (inline) return inline;

    const computed = normalizeToHexOrEmpty(
        getComputedStyleForPage(page).getPropertyValue(
            "--pageNumber-background-color",
        ),
    );
    return computed || "";
};

const setPageNumberBackgroundColor = (color: string): void => {
    const page = getCurrentPageElement();
    setOrRemoveCustomProperty(
        page.style,
        "--pageNumber-background-color",
        color,
    );
};

export const getCurrentPageSettings = (): IPageSettings => {
    return {
        page: {
            backgroundColor: getCurrentPageBackgroundColor(),
            pageNumberColor: getPageNumberColor(),
            pageNumberBackgroundColor: getPageNumberBackgroundColor(),
        },
    };
};

export const applyPageSettings = (settings: IPageSettings): void => {
    setCurrentPageBackgroundColor(settings.page.backgroundColor);
    setPageNumberColor(settings.page.pageNumberColor);
    setPageNumberBackgroundColor(settings.page.pageNumberBackgroundColor);
};

export const parsePageSettingsFromConfigrValue = (
    value: unknown,
): IPageSettings => {
    const parsed = typeof value === "string" ? JSON.parse(value) : value;
    if (typeof parsed !== "object" || !parsed) {
        throw new Error("Page settings are not an object");
    }
    const parsedRecord = parsed as Record<string, unknown>;
    const pageValues = parsedRecord["page"];

    if (typeof pageValues !== "object" || !pageValues) {
        throw new Error("Page settings are missing the page object");
    }

    const pageRecord = pageValues as Record<string, unknown>;

    const backgroundColor = pageRecord["backgroundColor"];
    const pageNumberColor = pageRecord["pageNumberColor"];
    const pageNumberBackgroundColor = pageRecord["pageNumberBackgroundColor"];

    if (
        typeof backgroundColor !== "string" ||
        typeof pageNumberColor !== "string" ||
        typeof pageNumberBackgroundColor !== "string"
    ) {
        throw new Error("Page settings are missing one or more color values");
    }

    return {
        page: {
            backgroundColor,
            pageNumberColor,
            pageNumberBackgroundColor,
        },
    };
};

export const arePageSettingsEquivalent = (
    first: IPageSettings,
    second: IPageSettings,
): boolean => {
    return (
        normalizeToHexOrEmpty(first.page.backgroundColor) ===
            normalizeToHexOrEmpty(second.page.backgroundColor) &&
        normalizeToHexOrEmpty(first.page.pageNumberColor) ===
            normalizeToHexOrEmpty(second.page.pageNumberColor) &&
        normalizeToHexOrEmpty(first.page.pageNumberBackgroundColor) ===
            normalizeToHexOrEmpty(second.page.pageNumberBackgroundColor)
    );
};

const PageBackgroundColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
    onColorPickerVisibilityChanged?: (open: boolean) => void;
}> = (props) => {
    const backgroundColorLabel = useL10n(
        "Background Color",
        "Common.BackgroundColor",
    );

    return (
        <ColorDisplayButton
            disabled={props.disabled}
            initialColor={props.value}
            localizedTitle={backgroundColorLabel}
            transparency={false}
            palette={BloomPalette.PageColors}
            width={75}
            onColorPickerVisibilityChanged={
                props.onColorPickerVisibilityChanged
            }
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
            onChange={(newColor) => props.onChange(newColor)}
        />
    );
};

const PageNumberColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
    onColorPickerVisibilityChanged?: (open: boolean) => void;
}> = (props) => {
    const pageNumberColorLabel = useL10n(
        "Page Number Color",
        "PageSettings.PageNumberColor",
    );

    return (
        <ColorDisplayButton
            disabled={props.disabled}
            initialColor={props.value}
            localizedTitle={pageNumberColorLabel}
            transparency={false}
            palette={BloomPalette.Text}
            width={75}
            onColorPickerVisibilityChanged={
                props.onColorPickerVisibilityChanged
            }
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
            onChange={(newColor) => props.onChange(newColor)}
        />
    );
};

const PageNumberBackgroundColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
    onColorPickerVisibilityChanged?: (open: boolean) => void;
}> = (props) => {
    const pageNumberBackgroundColorLabel = useL10n(
        "Page Number Background Color",
        "PageSettings.PageNumberBackgroundColor",
    );

    return (
        <ColorDisplayButton
            disabled={props.disabled}
            initialColor={props.value || "transparent"}
            localizedTitle={pageNumberBackgroundColorLabel}
            transparency={true}
            palette={BloomPalette.PageColors}
            width={75}
            onColorPickerVisibilityChanged={
                props.onColorPickerVisibilityChanged
            }
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
            onChange={(newColor) => props.onChange(newColor)}
        />
    );
};

const PageSettingsConfigrInputs: React.FunctionComponent<{
    disabled?: boolean;
    onColorPickerVisibilityChanged?: (open: boolean) => void;
}> = (props) => {
    const backgroundColorLabel = useL10n(
        "Background Color",
        "Common.BackgroundColor",
    );
    const pageNumberColorLabel = useL10n(
        "Page Number Color",
        "PageSettings.PageNumberColor",
    );
    const pageNumberBackgroundColorLabel = useL10n(
        "Page Number Background Color",
        "PageSettings.PageNumberBackgroundColor",
    );

    const pageBackgroundColorControl = React.useCallback(
        (pickerProps: {
            value: string;
            disabled?: boolean;
            onChange: (value: string) => void;
        }) => (
            <PageBackgroundColorPickerForConfigr
                {...pickerProps}
                onColorPickerVisibilityChanged={
                    props.onColorPickerVisibilityChanged
                }
            />
        ),
        [props.onColorPickerVisibilityChanged],
    );

    const pageNumberColorControl = React.useCallback(
        (pickerProps: {
            value: string;
            disabled?: boolean;
            onChange: (value: string) => void;
        }) => (
            <PageNumberColorPickerForConfigr
                {...pickerProps}
                onColorPickerVisibilityChanged={
                    props.onColorPickerVisibilityChanged
                }
            />
        ),
        [props.onColorPickerVisibilityChanged],
    );

    const pageNumberBackgroundColorControl = React.useCallback(
        (pickerProps: {
            value: string;
            disabled?: boolean;
            onChange: (value: string) => void;
        }) => (
            <PageNumberBackgroundColorPickerForConfigr
                {...pickerProps}
                onColorPickerVisibilityChanged={
                    props.onColorPickerVisibilityChanged
                }
            />
        ),
        [props.onColorPickerVisibilityChanged],
    );

    return (
        <>
            <ConfigrCustomStringInput
                label={backgroundColorLabel}
                path={"page.backgroundColor"}
                control={pageBackgroundColorControl}
                disabled={props.disabled ?? false}
            />
            <ConfigrCustomStringInput
                label={pageNumberColorLabel}
                path={"page.pageNumberColor"}
                control={pageNumberColorControl}
                disabled={props.disabled ?? false}
            />
            <ConfigrCustomStringInput
                label={pageNumberBackgroundColorLabel}
                path={"page.pageNumberBackgroundColor"}
                control={pageNumberBackgroundColorControl}
                disabled={props.disabled ?? false}
            />
        </>
    );
};

export type IPageSettingsAreaDefinition = {
    label: string;
    pageKey: string;
    content: string;
    pages: React.ReactElement[];
};

export const usePageSettingsAreaDefinition = (props: {
    onColorPickerVisibilityChanged?: (open: boolean) => void;
}): IPageSettingsAreaDefinition => {
    const pageAreaLabel = useL10n("Page", "BookAndPageSettings.PageArea");
    const colorsPageLabel = useL10n("Colors", "BookAndPageSettings.Colors");
    const pageAreaDescription = useL10n(
        "Page settings apply to the current page.",
        "BookAndPageSettings.PageArea.Description",
    );

    return {
        label: pageAreaLabel,
        pageKey: "pageArea",
        content: pageAreaDescription,
        pages: [
            <ConfigrPage key="colors" label={colorsPageLabel} pageKey="colors">
                <ConfigrGroup label={""}>
                    <PageSettingsConfigrInputs
                        disabled={false}
                        onColorPickerVisibilityChanged={
                            props.onColorPickerVisibilityChanged
                        }
                    />
                </ConfigrGroup>
            </ConfigrPage>,
        ],
    };
};
