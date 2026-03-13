// Text-oriented canvas control menu builders.
//
// These controls need imperative DOM updates and API calls, so they live here
// instead of expanding the registry module with implementation-heavy helpers.

import * as React from "react";
import { default as CheckIcon } from "@mui/icons-material/Check";
import { getString, postThatMightNavigate } from "../../../utils/bloomApi";
import { getCanvasElementManager } from "./canvasElementPageBridge";
import { IControlContext, IControlMenuCommandRow } from "./canvasControlTypes";

const fieldsControlledByAppearanceSystem = ["bookTitle"];

const fieldTypeData: Array<{
    dataBook: string;
    dataDerived: string;
    label: string;
    readOnly: boolean;
    editableClasses: string[];
    classes: string[];
    hint?: string;
    functionOnHintClick?: string;
}> = [
    {
        dataBook: "bookTitle",
        dataDerived: "",
        label: "Book Title",
        readOnly: false,
        editableClasses: ["Title-On-Cover-style", "bloom-padForOverflow"],
        classes: ["bookTitle"],
    },
    {
        dataBook: "smallCoverCredits",
        dataDerived: "",
        label: "Cover Credits",
        readOnly: false,
        editableClasses: ["smallCoverCredits", "Cover-Default-style"],
        classes: [],
    },
    {
        dataBook: "",
        dataDerived: "languagesOfBook",
        label: "Languages",
        readOnly: true,
        editableClasses: [],
        classes: ["coverBottomLangName", "Cover-Default-style"],
    },
    {
        dataBook: "",
        dataDerived: "topic",
        label: "Topic",
        readOnly: true,
        editableClasses: [],
        classes: [
            "coverBottomBookTopic",
            "bloom-userCannotModifyStyles",
            "bloom-alwaysShowBubble",
            "Cover-Default-style",
        ],
        hint: "Click to choose topic",
        functionOnHintClick: "showTopicChooser",
    },
];

function removeClassesByPrefix(element: HTMLElement, prefix: string): void {
    Array.from(element.classList).forEach((className) => {
        if (className.startsWith(prefix)) {
            element.classList.remove(className);
        }
    });
}

function updateTranslationGroupLanguage(
    translationGroup: HTMLElement,
    langCode: string,
    langName: string,
    dataDefaultLang: string,
    classes: string[],
    appearanceClasses: string[],
): void {
    translationGroup.setAttribute("data-default-languages", dataDefaultLang);
    const editables = Array.from(
        translationGroup.getElementsByClassName("bloom-editable"),
    ) as HTMLElement[];
    if (editables.length === 0) {
        return;
    }

    let editableInLang = editables.find(
        (editableElement) => editableElement.getAttribute("lang") === langCode,
    );
    if (editableInLang) {
        editables.splice(editables.indexOf(editableInLang), 1);
    } else {
        let editableToClone = editables.find(
            (editableElement) => editableElement.getAttribute("lang") === "z",
        );
        if (!editableToClone) {
            editableToClone = editables[0];
        }
        editableInLang = editableToClone.cloneNode(true) as HTMLElement;
        editableInLang.innerHTML = "<p><br></p>";
        editableInLang.setAttribute("lang", langCode);
        editableInLang.setAttribute("data-languagetipcontent", langName);
        translationGroup.appendChild(editableInLang);
    }

    removeClassesByPrefix(editableInLang, "bloom-content");
    removeClassesByPrefix(editableInLang, "bloom-visibility-code-");
    editableInLang.classList.add("bloom-visibility-code-on");
    editables.forEach((editableElement) => {
        removeClassesByPrefix(editableElement, "bloom-visibility-code-");
        editableElement.classList.add("bloom-visibility-code-off");
    });
    classes.forEach((className) => editableInLang?.classList.add(className));

    const dataBookValue = editableInLang.getAttribute("data-book");
    if (
        dataBookValue &&
        fieldsControlledByAppearanceSystem.includes(dataBookValue)
    ) {
        appearanceClasses.forEach((className) =>
            editableInLang?.classList.add(className),
        );
    }
}

function clearFieldTypeClasses(translationGroup: HTMLElement): void {
    fieldTypeData.forEach((fieldType) => {
        fieldType.classes.forEach((className) => {
            translationGroup.classList.remove(className);
        });
        Array.from(
            translationGroup.getElementsByClassName("bloom-editable"),
        ).forEach((editable) => {
            (editable as HTMLElement).classList.remove(
                ...fieldType.editableClasses,
            );
        });
    });
}

export function makeLanguageMenuItem(
    ctx: IControlContext,
): IControlMenuCommandRow {
    const translationGroup = ctx.canvasElement.getElementsByClassName(
        "bloom-translationGroup",
    )[0] as HTMLElement | undefined;
    const visibleEditable = translationGroup?.querySelector(
        ".bloom-editable.bloom-visibility-code-on",
    ) as HTMLElement | undefined;
    const activeLangTag =
        visibleEditable?.getAttribute("lang") ??
        (
            translationGroup?.getElementsByClassName("bloom-editable")[0] as
                | HTMLElement
                | undefined
        )?.getAttribute("lang");
    const { languageNameValues } = ctx;

    const subMenuItems: IControlMenuCommandRow[] = [
        {
            id: "language",
            englishLabel: languageNameValues.language1Name,
            icon:
                activeLangTag === languageNameValues.language1Tag
                    ? React.createElement(CheckIcon, null)
                    : undefined,
            onSelect: (rowCtx) => {
                if (!translationGroup) {
                    return;
                }
                updateTranslationGroupLanguage(
                    translationGroup,
                    languageNameValues.language1Tag,
                    languageNameValues.language1Name,
                    "V",
                    ["bloom-content1"],
                    ["bloom-contentFirst"],
                );
                getCanvasElementManager()?.setActiveElement(
                    rowCtx.canvasElement,
                );
            },
        },
    ];

    if (
        languageNameValues.language2Tag &&
        languageNameValues.language2Tag !== languageNameValues.language1Tag
    ) {
        subMenuItems.push({
            id: "language",
            englishLabel: languageNameValues.language2Name,
            icon:
                activeLangTag === languageNameValues.language2Tag
                    ? React.createElement(CheckIcon, null)
                    : undefined,
            onSelect: (rowCtx) => {
                if (!translationGroup) {
                    return;
                }
                updateTranslationGroupLanguage(
                    translationGroup,
                    languageNameValues.language2Tag,
                    languageNameValues.language2Name,
                    "N1",
                    ["bloom-contentNational1"],
                    ["bloom-contentSecond"],
                );
                getCanvasElementManager()?.setActiveElement(
                    rowCtx.canvasElement,
                );
            },
        });
    }

    if (
        languageNameValues.language3Tag &&
        languageNameValues.language3Tag !== languageNameValues.language1Tag &&
        languageNameValues.language3Tag !== languageNameValues.language2Tag
    ) {
        subMenuItems.push({
            id: "language",
            englishLabel: languageNameValues.language3Name,
            icon:
                activeLangTag === languageNameValues.language3Tag
                    ? React.createElement(CheckIcon, null)
                    : undefined,
            onSelect: (rowCtx) => {
                if (!translationGroup) {
                    return;
                }
                updateTranslationGroupLanguage(
                    translationGroup,
                    languageNameValues.language3Tag!,
                    languageNameValues.language3Name!,
                    "N2",
                    ["bloom-contentNational2"],
                    ["bloom-contentThird"],
                );
                getCanvasElementManager()?.setActiveElement(
                    rowCtx.canvasElement,
                );
            },
        });
    }

    return {
        id: "language",
        l10nId: "EditTab.Toolbox.ComicTool.Options.Language",
        englishLabel: "Language:",
        onSelect: () => {},
        subMenuItems,
    };
}

export function makeFieldTypeMenuItem(
    ctx: IControlContext,
): IControlMenuCommandRow {
    const translationGroup = ctx.canvasElement.getElementsByClassName(
        "bloom-translationGroup",
    )[0] as HTMLElement | undefined;
    const activeType = (
        translationGroup?.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on",
        )[0] as HTMLElement | undefined
    )?.getAttribute("data-book");
    const subMenuItems: IControlMenuCommandRow[] = [
        {
            id: "fieldType",
            l10nId: "EditTab.Toolbox.DragActivity.None",
            englishLabel: "None",
            icon: !activeType
                ? React.createElement(CheckIcon, null)
                : undefined,
            onSelect: () => {
                if (!translationGroup) {
                    return;
                }
                clearFieldTypeClasses(translationGroup);
                Array.from(
                    translationGroup.getElementsByClassName("bloom-editable"),
                ).forEach((editable) => {
                    const htmlEditable = editable as HTMLElement;
                    htmlEditable.removeAttribute("data-book");
                    htmlEditable.removeAttribute("data-derived");
                });
            },
        },
    ];

    fieldTypeData.forEach((fieldType) => {
        subMenuItems.push({
            id: "fieldType",
            englishLabel: fieldType.label,
            icon:
                activeType === fieldType.dataBook
                    ? React.createElement(CheckIcon, null)
                    : undefined,
            onSelect: () => {
                if (!translationGroup) {
                    return;
                }
                clearFieldTypeClasses(translationGroup);
                const editables = Array.from(
                    translationGroup.getElementsByClassName("bloom-editable"),
                ) as HTMLElement[];
                if (fieldType.readOnly) {
                    const readOnlyDiv = document.createElement("div");
                    readOnlyDiv.setAttribute(
                        "data-derived",
                        fieldType.dataDerived,
                    );
                    if (fieldType.hint) {
                        readOnlyDiv.setAttribute("data-hint", fieldType.hint);
                    }
                    if (fieldType.functionOnHintClick) {
                        readOnlyDiv.setAttribute(
                            "data-functiononhintclick",
                            fieldType.functionOnHintClick,
                        );
                    }
                    readOnlyDiv.classList.add(...fieldType.classes);
                    translationGroup.parentElement?.insertBefore(
                        readOnlyDiv,
                        translationGroup,
                    );
                    translationGroup.remove();
                    postThatMightNavigate(
                        "common/saveChangesAndRethinkPageEvent",
                    );
                    return;
                }

                translationGroup.classList.add(...fieldType.classes);
                editables.forEach((editable) => {
                    editable.classList.add(...fieldType.editableClasses);
                    editable.removeAttribute("data-derived");
                    editable.setAttribute("data-book", fieldType.dataBook);
                    if (
                        editable.classList.contains(
                            "bloom-visibility-code-on",
                        ) &&
                        fieldType.dataBook
                    ) {
                        getString(
                            `editView/getDataBookValue?lang=${editable.getAttribute("lang")}&dataBook=${fieldType.dataBook}`,
                            (content) => {
                                const temp = document.createElement("div");
                                temp.innerHTML = content || "";
                                if (temp.textContent.trim() !== "") {
                                    editable.innerHTML = content;
                                }
                            },
                        );
                    }
                });
            },
        });
    });

    return {
        id: "fieldType",
        l10nId: "EditTab.Toolbox.ComicTool.Options.FieldType",
        englishLabel: "Field:",
        onSelect: () => {},
        subMenuItems,
    };
}
