import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrCustomStringInput,
    ConfigrGroup,
    ConfigrPage,
    ConfigrPane,
} from "@sillsdev/config-r";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../../react_components/BloomDialog/BloomDialog";
import { useSetupBloomDialog } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../react_components/l10nHooks";
import {
    ColorDisplayButton,
    DialogResult,
} from "../../react_components/color-picking/colorPickerDialog";
import { BloomPalette } from "../../react_components/color-picking/bloomPalette";
import { ElementAttributeSnapshot } from "../../utils/ElementAttributeSnapshot";
import { getPageIframeBody } from "../../utils/shared";
import { ShowEditViewDialog } from "../editViewFrame";
import tinycolor from "tinycolor2";

let isOpenAlready = false;

type IPageSettings = {
    page: {
        backgroundColor: string;
        pageNumberColor: string;
        pageNumberBackgroundColor: string;
    };
};

const getCurrentPageElement = (): HTMLElement => {
    const page = getPageIframeBody()?.querySelector(
        ".bloom-page",
    ) as HTMLElement | null;
    if (!page) {
        throw new Error(
            "PageSettingsDialog could not find .bloom-page in the page iframe",
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

const parsePageSettingsFromConfigrValue = (value: unknown): IPageSettings => {
    if (typeof value === "string") {
        return JSON.parse(value) as IPageSettings;
    }

    if (typeof value === "object" && value) {
        return value as IPageSettings;
    }

    throw new Error(
        "PageSettingsDialog: unexpected value from config-r onChange",
    );
};

const arePageSettingsEquivalent = (
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

const applyPageSettings = (settings: IPageSettings): void => {
    setCurrentPageBackgroundColor(settings.page.backgroundColor);
    setPageNumberColor(settings.page.pageNumberColor);
    setPageNumberBackgroundColor(settings.page.pageNumberBackgroundColor);
};

const PageBackgroundColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled: boolean;
    onChange: (value: string) => void;
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
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
            onChange={(newColor) => props.onChange(newColor)}
        />
    );
};

const PageNumberColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled: boolean;
    onChange: (value: string) => void;
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
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
            onChange={(newColor) => props.onChange(newColor)}
        />
    );
};

const PageNumberBackgroundColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled: boolean;
    onChange: (value: string) => void;
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
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
            onChange={(newColor) => props.onChange(newColor)}
        />
    );
};

export const PageSettingsDialog: React.FunctionComponent = () => {
    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false,
    });

    const closeDialogAndClearOpenFlag = React.useCallback(() => {
        isOpenAlready = false;
        closeDialog();
    }, [closeDialog]);

    const pageSettingsTitle = useL10n("Page Settings", "PageSettings.Title");
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

    const [initialValues, setInitialValues] = React.useState<
        IPageSettings | undefined
    >(undefined);

    const [settingsToReturnLater, setSettingsToReturnLater] = React.useState<
        IPageSettings | string | undefined
    >(undefined);

    const initialPageAttributeSnapshot = React.useRef<
        ElementAttributeSnapshot | undefined
    >(undefined);

    // Read after mount so we get the current page's color even if opening this dialog
    // is preceded by a save/refresh that updates the page iframe.
    React.useEffect(() => {
        initialPageAttributeSnapshot.current =
            ElementAttributeSnapshot.fromElement(getCurrentPageElement());

        setInitialValues({
            page: {
                backgroundColor: getCurrentPageBackgroundColor(),
                pageNumberColor: getPageNumberColor(),
                pageNumberBackgroundColor: getPageNumberBackgroundColor(),
            },
        });
    }, []);

    const onOk = (): void => {
        const rawSettings = settingsToReturnLater ?? initialValues;
        if (!rawSettings) {
            throw new Error(
                "PageSettingsDialog: expected settings to be loaded before OK",
            );
        }

        const settings =
            typeof rawSettings === "string"
                ? (JSON.parse(rawSettings) as IPageSettings)
                : rawSettings;

        applyPageSettings(settings);
        closeDialogAndClearOpenFlag();
    };

    const onCancel = (
        _reason?:
            | "escapeKeyDown"
            | "backdropClick"
            | "titleCloseClick"
            | "cancelClicked",
    ): void => {
        if (initialPageAttributeSnapshot.current) {
            initialPageAttributeSnapshot.current.restoreToElement(
                getCurrentPageElement(),
            );
        }
        closeDialogAndClearOpenFlag();
    };

    const onClose = (
        _evt?: object,
        _reason?: "escapeKeyDown" | "backdropClick",
    ): void => {
        onCancel(_reason);
    };

    return (
        <BloomDialog
            {...propsForBloomDialog}
            onClose={onClose}
            onCancel={onCancel}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={pageSettingsTitle} />
            <DialogMiddle
                css={css`
                    padding-top: 10px;
                    padding-bottom: 10px;
                    form {
                        width: 420px;
                    }
                `}
            >
                {initialValues && (
                    <div
                        css={css`
                            width: 420px;
                        `}
                    >
                        <ConfigrPane
                            label={pageSettingsTitle}
                            initialValues={initialValues}
                            themeOverrides={{
                                palette: {
                                    primary: { main: kBloomBlue },
                                },
                            }}
                            showAppBar={false}
                            showJson={false}
                            onChange={(s: unknown) => {
                                const settings =
                                    parsePageSettingsFromConfigrValue(s);

                                if (
                                    !settingsToReturnLater &&
                                    initialValues &&
                                    arePageSettingsEquivalent(
                                        settings,
                                        initialValues,
                                    )
                                ) {
                                    return;
                                }

                                if (typeof s === "string") {
                                    setSettingsToReturnLater(s);
                                } else {
                                    setSettingsToReturnLater(settings);
                                }

                                applyPageSettings(settings);
                            }}
                        >
                            <ConfigrPage
                                label={pageSettingsTitle}
                                pageKey={"pageSettings"}
                                topLevel={true}
                            >
                                <ConfigrGroup label={""}>
                                    <ConfigrCustomStringInput
                                        label={backgroundColorLabel}
                                        path={"page.backgroundColor"}
                                        control={
                                            PageBackgroundColorPickerForConfigr
                                        }
                                        disabled={false}
                                    />
                                    <ConfigrCustomStringInput
                                        label={pageNumberColorLabel}
                                        path={"page.pageNumberColor"}
                                        control={
                                            PageNumberColorPickerForConfigr
                                        }
                                        disabled={false}
                                    />
                                    <ConfigrCustomStringInput
                                        label={pageNumberBackgroundColorLabel}
                                        path={"page.pageNumberBackgroundColor"}
                                        control={
                                            PageNumberBackgroundColorPickerForConfigr
                                        }
                                        disabled={false}
                                    />
                                </ConfigrGroup>
                            </ConfigrPage>
                        </ConfigrPane>
                    </div>
                )}
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton onClick={onOk} />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

export const showPageSettingsDialog = () => {
    if (!isOpenAlready) {
        isOpenAlready = true;
        ShowEditViewDialog(<PageSettingsDialog />);
    }
};
