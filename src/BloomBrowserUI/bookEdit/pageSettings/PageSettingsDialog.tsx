import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrCustomStringInput,
    ConfigrGroup,
    ConfigrPane,
    ConfigrSubgroup,
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

    const inline = normalizeToHexOrEmpty(
        page.style.getPropertyValue("--page-background-color"),
    );
    if (inline) return inline;

    const computedVariable = normalizeToHexOrEmpty(
        getComputedStyleForPage(page).getPropertyValue(
            "--page-background-color",
        ),
    );
    if (computedVariable) return computedVariable;

    const computedMarginBoxVariable = normalizeToHexOrEmpty(
        getComputedStyleForPage(page).getPropertyValue(
            "--marginBox-background-color",
        ),
    );
    if (computedMarginBoxVariable) return computedMarginBoxVariable;

    const computedBackground = normalizeToHexOrEmpty(
        getComputedStyleForPage(page).backgroundColor,
    );
    return computedBackground || "#FFFFFF";
};

const setCurrentPageBackgroundColor = (color: string): void => {
    const page = getCurrentPageElement();
    page.style.setProperty("--page-background-color", color);
    page.style.setProperty("--marginBox-background-color", color);
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
    page.style.setProperty("--pageNumber-color", color);
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
    page.style.setProperty("--pageNumber-background-color", color);
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
            palette={BloomPalette.CoverBackground}
            width={75}
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
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
            palette={BloomPalette.CoverBackground}
            width={75}
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
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
            palette={BloomPalette.CoverBackground}
            width={75}
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
        />
    );
};

export const PageSettingsDialog: React.FunctionComponent = () => {
    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false,
    });

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

    // Read after mount so we get the current page's color even if opening this dialog
    // is preceded by a save/refresh that updates the page iframe.
    React.useEffect(() => {
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

        setCurrentPageBackgroundColor(settings.page.backgroundColor);
        setPageNumberColor(settings.page.pageNumberColor);
        setPageNumberBackgroundColor(settings.page.pageNumberBackgroundColor);
        isOpenAlready = false;
        closeDialog();
    };

    const onCancel = (): void => {
        isOpenAlready = false;
        closeDialog();
    };

    return (
        <BloomDialog
            {...propsForBloomDialog}
            onClose={closeDialog}
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
                            showAllGroups={true}
                            themeOverrides={{
                                palette: {
                                    primary: { main: kBloomBlue },
                                },
                            }}
                            showAppBar={false}
                            showJson={false}
                            onChange={(s: unknown) => {
                                if (typeof s === "string") {
                                    setSettingsToReturnLater(s);
                                    return;
                                }

                                if (typeof s === "object" && s) {
                                    setSettingsToReturnLater(
                                        s as IPageSettings,
                                    );
                                    return;
                                }

                                throw new Error(
                                    "PageSettingsDialog: unexpected value from config-r onChange",
                                );
                            }}
                        >
                            <ConfigrGroup label={""} level={1}>
                                <ConfigrSubgroup label={""} path={"page"}>
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
                                </ConfigrSubgroup>
                            </ConfigrGroup>
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
