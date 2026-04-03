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
import { getBloomPageElement } from "../../utils/shared";

export type IPageSettings = {
    page: {
        backgroundColor: string;
        pageNumberColor: string;
        pageNumberOutlineColor: string;
        pageNumberBackgroundColor: string;
    };
};

export type IChangedPageSettings = {
    page: Partial<IPageSettings["page"]>;
};

export const getCurrentPageElement = (): HTMLElement => {
    const page = getBloomPageElement();
    if (!page) {
        throw new Error(
            "PageSettingsConfigrPages could not find .bloom-page in the page iframe",
        );
    }
    return page;
};

const kTransparentCssValue = "transparent";

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

const normalizeToHexOrTransparentOrEmpty = (color: string): string => {
    const trimmed = color.trim();
    if (!trimmed) {
        return "";
    }

    const parsed = tinycolor(trimmed);
    if (!parsed.isValid()) {
        return trimmed;
    }

    if (parsed.getAlpha() === 0) {
        return kTransparentCssValue;
    }

    if (parsed.getAlpha() < 1) {
        return parsed.toHex8String().toUpperCase();
    }

    return parsed.toHexString().toUpperCase();
};

const normalizeResolvedColorOrEmpty = (color: string): string => {
    const trimmed = color.trim();
    // Custom property inspection can return unresolved expressions like
    // var(--page-background-color). Those are implementation details, not the
    // user-facing color that the settings UI should round-trip.
    if (trimmed.startsWith("var(")) {
        return "";
    }

    return normalizeToHexOrEmpty(trimmed);
};

const getComputedStyleForElement = (
    element: HTMLElement,
): CSSStyleDeclaration => {
    const view = element.ownerDocument.defaultView;
    if (view) {
        return view.getComputedStyle(element);
    }
    return getComputedStyle(element);
};

const getCurrentPageBackgroundColor = (): string => {
    const page = getCurrentPageElement();
    const computedPageStyle = getComputedStyleForElement(page);

    // We cannot just read computedPageStyle.backgroundColor. In separated themes,
    // the outer .bloom-page shell stays theme-colored while the user-facing page
    // surface inside .marginBox has its own color, and the settings UI needs to
    // round-trip the persisted page-surface color rather than the shell color.
    const inlineMarginBox = normalizeResolvedColorOrEmpty(
        page.style.getPropertyValue("--marginBox-background-color"),
    );
    // Honor an inline marginBox override first for backwards compatibility with
    // older saved pages that persisted a page-specific marginBox color.
    if (inlineMarginBox) return inlineMarginBox;

    const inline = normalizeToHexOrEmpty(
        page.style.getPropertyValue("--page-background-color"),
    );
    // Next honor an inline page-surface override. This is the only page-level
    // background color the settings dialog now writes.
    if (inline) return inline;

    const computedMarginBoxVariable = normalizeResolvedColorOrEmpty(
        computedPageStyle.getPropertyValue("--marginBox-background-color"),
    );
    // If a theme supplies a literal marginBox color, honor it as the visible
    // page surface. If the variable is just an alias like var(--page-background-color),
    // normalizeResolvedColorOrEmpty() deliberately ignores it and we fall through
    // to the true resolved surface color below.
    if (computedMarginBoxVariable) return computedMarginBoxVariable;

    const computedVariable = normalizeToHexOrEmpty(
        computedPageStyle.getPropertyValue("--page-background-color"),
    );
    // For flat themes, the themed default often lives on the page-background
    // variable. Reading the variable preserves the intended setting without
    // depending on which element ultimately paints the background.
    if (computedVariable) return computedVariable;

    const marginBox = page.querySelector(".marginBox") as HTMLElement | null;
    if (marginBox) {
        const computedMarginBoxBackground = normalizeToHexOrEmpty(
            getComputedStyleForElement(marginBox).backgroundColor,
        );
        // Last resort: some CSS can paint the marginBox directly. If that
        // happens without a useful custom property on .bloom-page, read the
        // rendered marginBox background because that is still the visible page
        // surface the dialog is editing.
        if (computedMarginBoxBackground) return computedMarginBoxBackground;
    }

    const computedBackground = normalizeToHexOrEmpty(
        computedPageStyle.backgroundColor,
    );
    // Final fallback for pages that do not expose a background through the
    // custom properties or a distinct marginBox surface.
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

const setOrRemoveCustomPropertyAllowTransparent = (
    style: CSSStyleDeclaration,
    propertyName: string,
    value: string,
): void => {
    const normalized = normalizeToHexOrTransparentOrEmpty(value);
    if (normalized) {
        style.setProperty(propertyName, normalized);
    } else {
        style.removeProperty(propertyName);
    }
};

const setCurrentPageBackgroundColor = (color: string): void => {
    const page = getCurrentPageElement();

    // Page settings own the page surface color. The marginBox follows that by
    // default in theme CSS, so there is no need to persist a second page-level
    // color override for the same visible surface.
    setOrRemoveCustomProperty(page.style, "--page-background-color", color);
    page.style.removeProperty("--marginBox-background-color");
};

const getPageNumberColor = (): string => {
    const page = getCurrentPageElement();

    const inline = normalizeToHexOrEmpty(
        page.style.getPropertyValue("--pageNumber-color"),
    );
    if (inline) return inline;

    const computed = normalizeToHexOrEmpty(
        getComputedStyleForElement(page).getPropertyValue("--pageNumber-color"),
    );
    return computed || "#000000";
};

const setPageNumberColor = (color: string): void => {
    const page = getCurrentPageElement();
    setOrRemoveCustomProperty(page.style, "--pageNumber-color", color);
};

const getPageNumberOutlineColor = (): string => {
    const page = getCurrentPageElement();

    const inline = normalizeToHexOrTransparentOrEmpty(
        page.style.getPropertyValue("--pageNumber-outline-color"),
    );
    if (inline) return inline;

    const computed = normalizeToHexOrTransparentOrEmpty(
        getComputedStyleForElement(page).getPropertyValue(
            "--pageNumber-outline-color",
        ),
    );
    return computed || "#FFFFFF";
};

const setPageNumberOutlineColor = (color: string): void => {
    const page = getCurrentPageElement();
    setOrRemoveCustomPropertyAllowTransparent(
        page.style,
        "--pageNumber-outline-color",
        color,
    );
};

const getPageNumberBackgroundColor = (): string => {
    const page = getCurrentPageElement();

    const inline = normalizeToHexOrTransparentOrEmpty(
        page.style.getPropertyValue("--pageNumber-background-color"),
    );
    if (inline) return inline;

    const computed = normalizeToHexOrTransparentOrEmpty(
        getComputedStyleForElement(page).getPropertyValue(
            "--pageNumber-background-color",
        ),
    );
    if (computed) return computed;

    return kTransparentCssValue;
};

const setPageNumberBackgroundColor = (color: string): void => {
    const page = getCurrentPageElement();
    setOrRemoveCustomPropertyAllowTransparent(
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
            pageNumberOutlineColor: getPageNumberOutlineColor(),
            pageNumberBackgroundColor: getPageNumberBackgroundColor(),
        },
    };
};

export const applyPageSettings = (settings: IPageSettings): void => {
    setCurrentPageBackgroundColor(settings.page.backgroundColor);
    setPageNumberColor(settings.page.pageNumberColor);
    setPageNumberOutlineColor(settings.page.pageNumberOutlineColor);
    setPageNumberBackgroundColor(settings.page.pageNumberBackgroundColor);
};

export const getChangedPageSettings = (
    initialSettings: IPageSettings,
    currentSettings: IPageSettings,
): IChangedPageSettings | undefined => {
    const changedSettings: IChangedPageSettings["page"] = {};

    if (
        normalizeToHexOrEmpty(initialSettings.page.backgroundColor) !==
        normalizeToHexOrEmpty(currentSettings.page.backgroundColor)
    ) {
        changedSettings.backgroundColor = currentSettings.page.backgroundColor;
    }

    if (
        normalizeToHexOrEmpty(initialSettings.page.pageNumberColor) !==
        normalizeToHexOrEmpty(currentSettings.page.pageNumberColor)
    ) {
        changedSettings.pageNumberColor = currentSettings.page.pageNumberColor;
    }

    if (
        normalizeToHexOrTransparentOrEmpty(
            initialSettings.page.pageNumberOutlineColor,
        ) !==
        normalizeToHexOrTransparentOrEmpty(
            currentSettings.page.pageNumberOutlineColor,
        )
    ) {
        changedSettings.pageNumberOutlineColor =
            currentSettings.page.pageNumberOutlineColor;
    }

    if (
        normalizeToHexOrTransparentOrEmpty(
            initialSettings.page.pageNumberBackgroundColor,
        ) !==
        normalizeToHexOrTransparentOrEmpty(
            currentSettings.page.pageNumberBackgroundColor,
        )
    ) {
        changedSettings.pageNumberBackgroundColor =
            currentSettings.page.pageNumberBackgroundColor;
    }

    if (Object.keys(changedSettings).length === 0) {
        return undefined;
    }

    return {
        page: changedSettings,
    };
};

export const applyChangedPageSettings = (
    settings: IChangedPageSettings,
): void => {
    if (settings.page.backgroundColor !== undefined) {
        setCurrentPageBackgroundColor(settings.page.backgroundColor);
    }

    if (settings.page.pageNumberColor !== undefined) {
        setPageNumberColor(settings.page.pageNumberColor);
    }

    if (settings.page.pageNumberOutlineColor !== undefined) {
        setPageNumberOutlineColor(settings.page.pageNumberOutlineColor);
    }

    if (settings.page.pageNumberBackgroundColor !== undefined) {
        setPageNumberBackgroundColor(settings.page.pageNumberBackgroundColor);
    }
};

export const parsePageSettingsFromConfigrValue = (
    value: unknown,
): IPageSettings => {
    if (typeof value !== "object" || !value) {
        throw new Error("Page settings are not an object");
    }
    const parsedRecord = value as Record<string, unknown>;
    const pageValues = parsedRecord["page"];

    if (typeof pageValues !== "object" || !pageValues) {
        throw new Error("Page settings are missing the page object");
    }

    const pageRecord = pageValues as Record<string, unknown>;

    const backgroundColor = pageRecord["backgroundColor"];
    const pageNumberColor = pageRecord["pageNumberColor"];
    const pageNumberOutlineColor = pageRecord["pageNumberOutlineColor"];
    const pageNumberBackgroundColor = pageRecord["pageNumberBackgroundColor"];

    if (
        typeof backgroundColor !== "string" ||
        typeof pageNumberColor !== "string" ||
        typeof pageNumberOutlineColor !== "string" ||
        typeof pageNumberBackgroundColor !== "string"
    ) {
        throw new Error("Page settings are missing one or more color values");
    }

    return {
        page: {
            backgroundColor,
            pageNumberColor,
            pageNumberOutlineColor,
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
        normalizeToHexOrTransparentOrEmpty(
            first.page.pageNumberOutlineColor,
        ) ===
            normalizeToHexOrTransparentOrEmpty(
                second.page.pageNumberOutlineColor,
            ) &&
        normalizeToHexOrTransparentOrEmpty(
            first.page.pageNumberBackgroundColor,
        ) ===
            normalizeToHexOrTransparentOrEmpty(
                second.page.pageNumberBackgroundColor,
            )
    );
};

type IConfigrColorPickerControlProps = {
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
};

const ConfigrColorPickerControl: React.FunctionComponent<
    IConfigrColorPickerControlProps & {
        localizedTitle: string;
        transparency: boolean;
        palette: BloomPalette;
        emptyValueDisplayColor?: string;
        onColorPickerVisibilityChanged?: (open: boolean) => void;
    }
> = (props) => {
    const initialColor = props.value || props.emptyValueDisplayColor;

    return (
        <ColorDisplayButton
            disabled={props.disabled}
            initialColor={initialColor || ""}
            localizedTitle={props.localizedTitle}
            transparency={props.transparency}
            palette={props.palette}
            width={75}
            deferOnChangeUntilComplete={true}
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

const PageSettingsConfigrColorInput: React.FunctionComponent<{
    label: string;
    path: string;
    description?: string;
    localizedTitle: string;
    transparency: boolean;
    palette: BloomPalette;
    emptyValueDisplayColor?: string;
    disabled?: boolean;
    onColorPickerVisibilityChanged?: (open: boolean) => void;
}> = (props) => {
    const colorControl = React.useCallback(
        (pickerProps: IConfigrColorPickerControlProps) => (
            <ConfigrColorPickerControl
                {...pickerProps}
                localizedTitle={props.localizedTitle}
                transparency={props.transparency}
                palette={props.palette}
                emptyValueDisplayColor={props.emptyValueDisplayColor}
                onColorPickerVisibilityChanged={
                    props.onColorPickerVisibilityChanged
                }
            />
        ),
        [
            props.emptyValueDisplayColor,
            props.localizedTitle,
            props.onColorPickerVisibilityChanged,
            props.palette,
            props.transparency,
        ],
    );

    return (
        <ConfigrCustomStringInput
            label={props.label}
            path={props.path}
            description={props.description}
            control={colorControl}
            disabled={props.disabled ?? false}
        />
    );
};

const PageConfigrInputs: React.FunctionComponent<{
    disabled?: boolean;
    onColorPickerVisibilityChanged?: (open: boolean) => void;
}> = (props) => {
    const backgroundColorLabel = useL10n(
        "Background Color",
        "Common.BackgroundColor",
    );

    return (
        <PageSettingsConfigrColorInput
            label={backgroundColorLabel}
            path={"page.backgroundColor"}
            localizedTitle={backgroundColorLabel}
            transparency={false}
            palette={BloomPalette.PageColors}
            disabled={props.disabled ?? false}
            onColorPickerVisibilityChanged={
                props.onColorPickerVisibilityChanged
            }
        />
    );
};

/*
 * BL-15642: hide the page number color group for now.
 * We could add this back in the future, perhaps as a book settings feature
 * instead of a page settings feature.
 */
const PageNumberConfigrInputs: React.FunctionComponent<{
    disabled?: boolean;
    onColorPickerVisibilityChanged?: (open: boolean) => void;
}> = (props) => {
    const colorLabel = useL10n("Color", "Common.Color");
    const outlineColorLabel = useL10n(
        "Outline Color",
        "PageSettings.OutlineColor",
    );
    const outlineColorDescription = useL10n(
        "Use an outline color when the page number needs more contrast against the page.",
        "PageSettings.PageNumberOutlineColor.Description",
    );
    const backgroundColorLabel = useL10n(
        "Background Color",
        "Common.BackgroundColor",
    );
    const backgroundColorDescription = useL10n(
        "Use a page number background color when the theme puts the number inside a shape, for example a circle, and you want to specify the color of that shape.",
        "PageSettings.PageNumberBackgroundColor.Description",
    );

    return (
        <>
            <PageSettingsConfigrColorInput
                label={colorLabel}
                path={"page.pageNumberColor"}
                localizedTitle={colorLabel}
                transparency={true}
                palette={BloomPalette.Text}
                disabled={props.disabled ?? false}
                onColorPickerVisibilityChanged={
                    props.onColorPickerVisibilityChanged
                }
            />
            <PageSettingsConfigrColorInput
                label={outlineColorLabel}
                path={"page.pageNumberOutlineColor"}
                description={outlineColorDescription}
                localizedTitle={outlineColorLabel}
                transparency={true}
                palette={BloomPalette.Text}
                emptyValueDisplayColor={kTransparentCssValue}
                disabled={props.disabled ?? false}
                onColorPickerVisibilityChanged={
                    props.onColorPickerVisibilityChanged
                }
            />
            <PageSettingsConfigrColorInput
                label={backgroundColorLabel}
                path={"page.pageNumberBackgroundColor"}
                description={backgroundColorDescription}
                localizedTitle={backgroundColorLabel}
                transparency={true}
                palette={BloomPalette.PageColors}
                emptyValueDisplayColor={kTransparentCssValue}
                disabled={props.disabled ?? false}
                onColorPickerVisibilityChanged={
                    props.onColorPickerVisibilityChanged
                }
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
    const pageAreaLabel = useL10n(
        "Current Page",
        "BookAndPageSettings.PageArea",
    );
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
                    <PageConfigrInputs
                        disabled={false}
                        onColorPickerVisibilityChanged={
                            props.onColorPickerVisibilityChanged
                        }
                    />
                </ConfigrGroup>
                <ConfigrGroup label={"Page Number"}>
                    <PageNumberConfigrInputs
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
