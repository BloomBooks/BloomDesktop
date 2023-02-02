/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";
import * as ReactDOM from "react-dom";

import { get, postBoolean, postData } from "../utils/bloomApi";
import {
    BloomDialog,
    DialogTitle,
    DialogMiddle
} from "../react_components/BloomDialog/BloomDialog";
import { useL10n } from "../react_components/l10nHooks";
import { getToolboxBundleExports } from "../bookEdit/js/bloomFrames";
import { SelectedTemplatePageControls } from "./selectedTemplatePageControls";
import { ChooserPageGroup } from "./ChooserPageGroup";

interface IPageChooserdialogProps {
    forChooseLayout: boolean;
}

export interface IGroupData {
    templateBookFolderUrl: string;
    templateBookPath: string;
}

// To test the AddPage/ChangeLayout dialog in devtools, type 'editTabBundle.showPageChooserDialog(false)' in
// the Console. Substitute 'true' for the ChangeLayout dialog.

// latest version of the expected JSON initialization string (from PageTemplatesApi.HandleTemplatesRequest)
// "{\"defaultPageToSelect\":\"(guid of template page)\",
//   \"orientation\":\"landscape\",
//   \"groups\":[{\"templateBookFolderUrl\":\"/bloom/localhost/C$/BloomDesktop/DistFiles/factoryGroups/Templates/Basic Book\",
//                     \"templateBookUrl\":\"/bloom/localhost/C$/BloomDesktop/DistFiles/factoryGroups/Templates/Basic Book/Basic Book.htm\"}]}"

export const PageChooserDialog: React.FunctionComponent<IPageChooserdialogProps> = props => {
    const [open, setOpen] = useState(true);

    const closeDialog = () => {
        setOpen(false);
    };

    // If this is false, we get a react render loop and error.
    // Probably because BloomDialog's underlying MUI dialog is already draggable.
    // In any case, the resulting dialog IS draggable.
    const disableDragging = true;

    const key = props.forChooseLayout
        ? "EditTab.AddPageDialog.ChooseLayoutTitle"
        : "EditTab.AddPageDialog.Title";

    const english = props.forChooseLayout
        ? "Choose Different Layout..."
        : "Add Page...";

    const dialogTitle = useL10n(english, key);
    const [selectedTemplateUrl, setSelectedTemplateUrl] = useState<
        string | undefined
    >(undefined);
    const [enterpriseAvailable, setEnterpriseAvailable] = useState(false);
    const [orientation, setOrientation] = useState("Portrait");
    const [defaultPageToSelect, setDefaultPageToSelect] = useState<
        string | undefined
    >(undefined);
    const [templateBookUrls, setTemplateBookUrls] = useState<IGroupData[]>([]);
    const [selectedGridItem, setSelectedGridItem] = useState<
        HTMLDivElement | undefined
    >(undefined);

    useEffect(() => {
        get("settings/enterpriseEnabled", enterpriseResult => {
            setEnterpriseAvailable(enterpriseResult.data);
        });
    }, []);

    // Tell edit tab to disable everything when the dialog is up.
    // (Without this, the page list is not disabled since the modal
    // div only exists in the book pane. Once the whole edit tab is inside
    // one browser, this would not be necessary.)
    useEffect(() => {
        if (open === undefined) return;

        postBoolean("editView/setModalState", open);
    }, [open]);

    useEffect(() => {
        get("pageTemplates", result => {
            const templatesJSON = result.data;
            const initializationObject = templatesJSON;
            // If provided, this is the last page that was added with the AddPage dialog, we'll
            // have the dialog select it initially.
            const defaultPageId = initializationObject["defaultPageToSelect"];
            const arrayOfBookUrls = initializationObject["groups"].map(
                (group: IGroupData) => {
                    return {
                        templateBookFolderUrl: group.templateBookFolderUrl,
                        templateBookPath: group.templateBookPath
                    };
                }
            );
            setDefaultPageToSelect(defaultPageId);
            setTemplateBookUrls(arrayOfBookUrls);
            setOrientation(initializationObject["orientation"]);
        });
    }, []);

    const selectNewGridItem = (newlySelectedDiv: HTMLDivElement) => {
        // Mark any previously selected thumbnail as no longer selected.
        if (
            selectedGridItem !== undefined &&
            selectedGridItem.firstChild !== null
        ) {
            (selectedGridItem.firstChild as HTMLDivElement).classList.remove(
                "ui-selected"
            );
        }
        // Mark the new thumbnail as selected.
        if (newlySelectedDiv.firstChild !== null) {
            (newlySelectedDiv.firstChild as HTMLDivElement).classList.add(
                "ui-selected"
            );
        }

        // Scroll to show it (useful for original selection). So far this only scrolls DOWN
        // to make sure we can see the BOTTOM of the clicked item; that's good enough for when
        // we open the dialog and a far-down item is selected, and marginally helpful when we click
        // an item partly scrolled off the bottom. There's no way currently to select an item
        // that's entirely scrolled off the top, and it doesn't seem worth the complication
        // to force a partly-visible one at the top to become wholly visible.
        const container = newlySelectedDiv.closest(".groupDisplay");
        if (container) {
            container.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }

        // Updating 'selectedGridItem' will cause the large preview to display.
        // Localization will happen there, so we just send english strings from the page templates.
        setSelectedGridItem(newlySelectedDiv);
        const parentGroup = newlySelectedDiv.parentElement;
        if (!parentGroup) return; // won't ever happen
        setSelectedTemplateUrl(
            getAttributeStringSafely(parentGroup, "data-template-book")
        );
    };

    useEffect(() => {
        // Check if we're not ready to process things.
        if (templateBookUrls.length === 0) return;

        // Give React some time to process/create thumbs
        setTimeout(() => {
            const loadedThumbs = document.getElementsByClassName("gridItem");
            if (loadedThumbs.length === 0) {
                return; // not really loaded yet
            }
            if (defaultPageToSelect) {
                const defaultThumb = Array.from(loadedThumbs).filter(
                    th =>
                        th.getAttribute("data-page-id") === defaultPageToSelect
                );
                if (defaultThumb.length > 0) {
                    selectNewGridItem(defaultThumb[0] as HTMLDivElement);
                    return;
                }
            }
            // No default thumb to select. Select the first thumb instead.
            selectNewGridItem(loadedThumbs[0] as HTMLDivElement);
        }, 250);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [templateBookUrls.length, defaultPageToSelect]);

    const learnMoreLink = selectedGridItem
        ? getAttributeStringSafely(selectedGridItem, "data-help-link")
        : "";
    const getToolId = (gridItem: HTMLDivElement | undefined): string => {
        return gridItem
            ? getAttributeStringSafely(gridItem, "data-tool-id")
            : "";
    };

    const thumbnailClickHandler: React.MouseEventHandler<HTMLDivElement> = (
        event
    ): void => {
        const clickedDiv = event.target as HTMLDivElement;
        // 'clickedDiv' is the selection overlay that sits on top of the thumbnail
        // Select new thumbnail
        const newSelection = clickedDiv.parentElement as HTMLDivElement; // What used to be known as .gridItem
        if (newSelection === null) return; // paranoia
        selectNewGridItem(newSelection);
    };

    // Double-click handler should select a thumb and then do the default action, if possible.
    const thumbnailDoubleClickHandler: React.MouseEventHandler<HTMLDivElement> = (
        event
    ): void => {
        const clickedDiv = event.target as HTMLDivElement;
        // 'clickedDiv' is the selection overlay that sits on top of the thumbnail
        // Select new thumbnail
        const newSelection = clickedDiv.parentElement as HTMLDivElement; // What used to be known as .gridItem
        if (newSelection === null) return; // paranoia
        selectNewGridItem(newSelection);
        const pageIsEnterpriseOnly = newSelection.classList.contains(
            "enterprise-only-flag"
        );
        if (pageIsEnterpriseOnly && !enterpriseAvailable) {
            return;
        }
        const convertAnywayCheckbox = document.getElementById(
            "convertAnywayCheckbox"
        ) as HTMLInputElement;
        const convertWholeBookCheckbox = document.getElementById(
            "convertWholeBookCheckbox"
        ) as HTMLInputElement;
        const closestGroupAncestor = newSelection.closest(".gridGroup");
        const bookPath = getAttributeStringSafely(
            closestGroupAncestor,
            "data-template-book"
        );
        const pageId = getAttributeStringSafely(newSelection, "data-page-id");
        handleAddPageOrChooseLayoutButtonClick(
            props.forChooseLayout,
            pageId,
            bookPath,
            convertAnywayCheckbox ? convertAnywayCheckbox.checked : false,
            willLoseData(newSelection),
            convertWholeBookCheckbox ? convertWholeBookCheckbox.checked : false,
            props.forChooseLayout ? -1 : 1,
            getToolId(newSelection)
        );
    };

    const getGroupData = (): JSX.Element[] | undefined => {
        if (templateBookUrls.length === 0) {
            return undefined;
        }
        return templateBookUrls.map((groupData, index) => {
            return (
                <ChooserPageGroup
                    groupUrls={groupData}
                    orientation={orientation}
                    forChooseLayout={props.forChooseLayout}
                    key={index}
                    onThumbSelect={thumbnailClickHandler}
                    onThumbDoubleClick={thumbnailDoubleClickHandler}
                />
            );
        });
    };

    // Return true if choosing the current layout will cause loss of data
    const willLoseData = (gridItem: HTMLDivElement | undefined): boolean => {
        if (gridItem === undefined) {
            return true;
        }
        const selectedTemplateTranslationGroupCount = parseInt(
            getAttributeStringSafely(gridItem, "data-text-div-count"),
            10
        );
        const selectedTemplatePictureCount = parseInt(
            getAttributeStringSafely(gridItem, "data-picture-count"),
            10
        );
        const selectedTemplateVideoCount = parseInt(
            getAttributeStringSafely(gridItem, "data-video-count"),
            10
        );
        const selectedTemplateWidgetCount = parseInt(
            getAttributeStringSafely(gridItem, "data-widget-count"),
            10
        );

        const page = window.parent.document.getElementById(
            "page"
        ) as HTMLIFrameElement;
        const current =
            page && page.contentWindow
                ? page.contentWindow.document.body
                : undefined;
        if (current === undefined) {
            return true;
        }
        const currentTranslationGroupCount = countTranslationGroupsForChangeLayout(
            current
        );
        const currentPictureCount = countEltsOfClassNotInImageContainer(
            current,
            "bloom-imageContainer"
        );
        // ".bloom-videoContainer:not(.bloom-noVideoSelected)" is not working reliably as a selector.
        // It's also insufficient if we allow the user to change multiple pages at once to look at
        // only the current page for content.  Not checking for actual video content matches what is
        // done for text and pictures, and means that the check is equally valid for any number of
        // pages with the same layout.  See https://issues.bloomlibrary.org/youtrack/issue/BL-6921.
        const currentVideoCount = countEltsOfClassNotInImageContainer(
            current,
            "bloom-videoContainer"
        );
        const currentWidgetCount = countEltsOfClassNotInImageContainer(
            current,
            "bloom-widgetContainer"
        );

        return (
            selectedTemplateTranslationGroupCount <
                currentTranslationGroupCount ||
            selectedTemplatePictureCount < currentPictureCount ||
            selectedTemplateVideoCount < currentVideoCount ||
            selectedTemplateWidgetCount < currentWidgetCount
        );
    };

    // This method handles the button click in the Add Page or Change Layout dialog.
    // It gets passed to the React component that displays the page preview and deals with all the
    // various checkbox logic.
    const handleAddPageOrChooseLayoutButtonClick = (
        forChangeLayout: boolean,
        pageId: string,
        templateBookPath: string,
        convertAnywayChecked: boolean,
        willLoseData: boolean,
        convertWholeBookChecked: boolean,
        numberToAdd: number,
        requiredTool?: string
    ): void => {
        if (forChangeLayout) {
            if (willLoseData && !convertAnywayChecked) {
                return;
            }
            postData("changeLayout", {
                pageId: pageId,
                templateBookPath: templateBookPath,
                convertWholeBook: convertWholeBookChecked,
                numberToAdd: 1, // meaningless here, but prevents throwing an exception in C#
                allowDataLoss: convertAnywayChecked
            });
        } else {
            postData("addPage", {
                templateBookPath: templateBookPath,
                pageId: pageId,
                convertWholeBook: false, // meaningless here, but keeps C# happy
                numberToAdd: numberToAdd,
                allowDataLoss: convertAnywayChecked // meaningless here, but keeps C# happy
            });
        }
        if (requiredTool) {
            // If we added/changed a page that requires a certain Toolbox tool, we will need to
            // make sure the Toolbox is open.
            const toolbox = getToolboxBundleExports()?.getTheOneToolbox();
            if (!toolbox) return; // Shouldn't happen; paranoia.
            // We make sure 'requiredTool' is checked and open.
            toolbox.activateToolFromId(requiredTool);
        }
        closeDialog();
    };

    return (
        <BloomDialog
            open={open}
            onClose={() => {
                closeDialog();
            }}
            css={css`
                padding-left: 18px;
                padding-bottom: 20px;
                width: 875px;
                height: 690px;
                background-color: unset; // BloomDialog default is somehow not what we want...
                .MuiDialog-paperWidthSm {
                    max-width: 925px;
                }
                .MuiDialog-paper {
                    margin: 0;
                    background-color: #f0f0f0;
                }
            `}
        >
            <DialogTitle
                title={dialogTitle}
                disableDragging={disableDragging}
                backgroundColor={"white"}
            />
            <DialogMiddle
                css={css`
                    border: none;
                    margin: 0;
                    height: 600px;
                    display: flex;
                    flex-direction: row;
                    box-sizing: border-box;
                    padding-top: 10px;
                `}
            >
                <div
                    className="groupDisplay"
                    css={css`
                        overflow-y: auto;
                        height: auto;
                        flex: 2;
                        display: flex;
                        flex-direction: column;
                        max-width: 425px;
                    `}
                >
                    {templateBookUrls.length > 0 && getGroupData()}
                </div>
                <div
                    css={css`
                        flex: 2;
                        order: 2;
                        height: auto;
                        min-width: 370px;
                        max-width: 420px; // in the standard dialog width, allows the other pane to be wide enough for 3 columns with scroll bar
                        display: flex;
                        flex-direction: row;
                        align-items: center;
                    `}
                >
                    {selectedGridItem && (
                        <SelectedTemplatePageControls
                            caption={getAttributeStringSafely(
                                selectedGridItem,
                                "data-page-label"
                            )}
                            pageDescription={getAttributeStringSafely(
                                selectedGridItem,
                                "data-page-description"
                            )}
                            pageId={getAttributeStringSafely(
                                selectedGridItem,
                                "data-page-id"
                            )}
                            imageSource={getAttributeStringSafely(
                                selectedGridItem.getElementsByTagName("img")[0],
                                "src"
                            )}
                            enterpriseAvailable={enterpriseAvailable}
                            pageIsEnterpriseOnly={
                                getAttributeStringSafely(
                                    selectedGridItem,
                                    "data-is-enterprise"
                                ) === "true"
                            }
                            pageIsDigitalOnly={
                                getAttributeStringSafely(
                                    selectedGridItem,
                                    "data-digital-only"
                                ) === "true"
                            }
                            templateBookPath={
                                selectedTemplateUrl ? selectedTemplateUrl : ""
                            }
                            isLandscape={orientation === "landscape"}
                            forChangeLayout={props.forChooseLayout}
                            willLoseData={
                                props.forChooseLayout
                                    ? willLoseData(selectedGridItem)
                                    : false
                            }
                            learnMoreLink={learnMoreLink}
                            requiredTool={getToolId(selectedGridItem)}
                            onSubmit={handleAddPageOrChooseLayoutButtonClick}
                        />
                    )}
                </div>
            </DialogMiddle>
        </BloomDialog>
    );
};

export function showPageChooserDialog(forChooseLayout: boolean) {
    try {
        ReactDOM.render(
            <PageChooserDialog forChooseLayout={forChooseLayout} />,
            getModalContainer()
        );
    } catch (error) {
        console.error(error);
    }
}

function getModalContainer(): HTMLElement {
    // If our container already exists, remove it and create a new one. Otherwise it maintains the
    // state of the last time, which may not be what we want (e.g. dialog title).
    let modalDialogContainer = document.getElementById(
        "PageChooserDialogContainer"
    );
    if (modalDialogContainer) {
        modalDialogContainer.remove();
    }
    modalDialogContainer = document.createElement("div");
    modalDialogContainer.id = "PageChooserDialogContainer";
    document.body.appendChild(modalDialogContainer);
    return modalDialogContainer;
}

// Utility functions used by both PageChooserDialog and ChooserPageGroup

// "Safely" from a type-checking point of view. The calling code is responsible to make sure
// that empty string is handled.
export function getAttributeStringSafely(
    element: Element | null,
    attributeName: string
): string {
    if (!element || !element.hasAttribute(attributeName)) {
        return "";
    }
    const value = element.getAttribute(attributeName);
    return value ? value : "";
}

// We want to count all the translationGroups that do not occur inside of a bloom-imageContainer div.
// The reason for this is that images can have textOverPicture divs and imageDescription divs inside of them
// and these are completely independent of the template page. We need to count regular translationGroups and
// also ensure that translationGroups inside of images get migrated correctly. If this algorithm changes, be
// sure to also change 'GetTranslationGroupsInternal()' in HtmlDom.cs.
export function countTranslationGroupsForChangeLayout(
    pageDiv: HTMLElement
): number {
    const allTranslationGroups = pageDiv.querySelectorAll(
        ".bloom-translationGroup:not(.box-header-off)"
    );
    return Array.from(allTranslationGroups).filter(
        translationGroup =>
            translationGroup.closest(".bloom-imageContainer") === null
    ).length;
}

export function countEltsOfClassNotInImageContainer(
    currentPageDiv: HTMLElement,
    className: string
): number {
    return (
        (Array.from(
            currentPageDiv.getElementsByClassName(className)
        ) as HTMLElement[])
            // filter out the ones inside an image container (but not ones that ARE image containers,
            // since that might be the class we're looking for.)
            .filter(
                e => e.parentElement?.closest(".bloom-imageContainer") === null
            ).length
    );
}
