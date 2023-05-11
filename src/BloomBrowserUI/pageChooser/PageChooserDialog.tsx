/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useCallback, useEffect, useState } from "react";

import { get, postBoolean, postData } from "../utils/bloomApi";
import WebSocketManager from "../utils/WebSocketManager";
import {
    BloomDialog,
    DialogTitle,
    DialogMiddle
} from "../react_components/BloomDialog/BloomDialog";
import { useL10n } from "../react_components/l10nHooks";
import { getBloomApiPrefix } from "../utils/bloomApi";
import { getToolboxBundleExports } from "../bookEdit/js/bloomFrames";
import SelectedTemplatePageControls from "./selectedTemplatePageControls";
import TemplateBookPages from "./TemplateBookPages";
import { useEnterpriseAvailable } from "../react_components/requiresBloomEnterprise";
import { ShowEditViewDialog } from "../bookEdit/editViewFrame";

interface IPageChooserdialogProps {
    forChooseLayout: boolean;
}

export interface IGroupData {
    templateBookFolderUrl: string;
    templateBookPath: string;
}

export const getPageLabel = (templatePageDiv: HTMLDivElement): string => {
    const labelList = templatePageDiv.getElementsByClassName("pageLabel");
    return labelList.length > 0
        ? (labelList[0] as HTMLDivElement).innerText.trim()
        : "";
};

export const getTemplatePageImageSource = (
    templateBookFolderUrl: string,
    pageLabel: string,
    orientation: string
): string => {
    const label = pageLabel.replace("&", "+"); //ampersands confuse the url system (if you don't handle them), so the template files were originally named with "+" instead of "&"
    // The result may actually be a png file or an svg, and there may be some delay while the png is generated.

    const urlPrefix = getBloomApiPrefix();

    //NB:  without the generateThumbnaiIfNecessary=true, we can run out of worker threads and get deadlocked.
    //See EnhancedImageServer.IsRecursiveRequestContext
    return (
        `${urlPrefix}pageTemplateThumbnail/` +
        encodeURIComponent(templateBookFolderUrl) +
        "/template/" +
        encodeURIComponent(label) +
        (orientation === "landscape"
            ? "-landscape"
            : orientation === "square"
            ? "-square"
            : "") +
        ".svg?generateThumbnaiIfNecessary=true"
    );
};

// To test the AddPage/ChangeLayout dialog in devtools, type 'editTabBundle.showPageChooserDialog(false)' in
// the Console. Substitute 'true' for the ChangeLayout dialog.

// latest version of the expected JSON initialization string (from PageTemplatesApi.HandleTemplatesRequest)
// "{\"defaultPageToSelect\":\"(guid of template page)\",
//   \"orientation\":\"landscape\",
//   \"groups\":[{\"templateBookFolderUrl\":\"/bloom/localhost/C$/BloomDesktop/DistFiles/factoryGroups/Templates/Basic Book\",
//                     \"templateBookUrl\":\"/bloom/localhost/C$/BloomDesktop/DistFiles/factoryGroups/Templates/Basic Book/Basic Book.htm\"}]}"

export const PageChooserDialog: React.FunctionComponent<IPageChooserdialogProps> = props => {
    const [open, setOpen] = useState(true);
    const [redoCounter, setRedoCounter] = useState(0);

    const closeDialog = () => {
        WebSocketManager.closeSocket("page-chooser");
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
    const [selectedTemplateBookPath, setSelectedTemplateBookPath] = useState<
        string | undefined
    >(undefined);
    const [defaultPageId, setDefaultPageId] = useState<string | undefined>(
        undefined
    );
    const [orientation, setOrientation] = useState("Portrait");
    const [templateBookUrls, setTemplateBookUrls] = useState<IGroupData[]>([]);
    const [selectedTemplatePageDiv, setSelectedTemplatePageDiv] = useState<
        HTMLDivElement | undefined
    >(undefined);

    // In order to scroll predictably to the last page added when we open the dialog, we must ensure that
    // of the axios calls to get the pages of the various templates have returned BEFORE we try to scroll.
    // To do that, we count all the template books and make sure they've all returned (successfully or not),
    // before we set allPagesLoaded, which triggers the scrolling.
    const [templateBooksLoadedCount, setTemplateBooksLoadedCount] = useState(0);
    const [allPagesLoaded, setAllPagesLoaded] = useState(false);

    const isEnterpriseAvailable = useEnterpriseAvailable();

    // Tell edit tab to disable everything when the dialog is up.
    // (Without this, the page list is not disabled since the modal
    // div only exists in the book pane. Once the whole edit tab is inside
    // one browser, this would not be necessary.)
    useEffect(() => {
        if (open === undefined) return;

        postBoolean("editView/setModalState", open);
    }, [open]);

    // The purpose of this callback:
    //   1) If TemplateBookPages goes to find a template page .svg file, and there isn't one for a
    //      particular page, the thumbnailer will attempt to generate one.
    //   2) When the thumbnailer returns from its task, this allows us to display the newly created
    //      thumbnail.
    const thumbnailUpdatedListener = useCallback(
        e => {
            if (e.id !== "thumbnail-updated") {
                return;
            }
            const updatedThumbUrl = (e as any).src as string;
            const images = document.getElementsByTagName("img");
            let internalCounter = redoCounter;
            for (let i = 0; i < images.length; i++) {
                const img = images[i];
                const imgSrc = img.src.replace(/%2F/g, "/");
                // The 'if' condition here is to make sure we're dealing with the correct image element.
                // We use 'startsWith' because the original 'imgSrc' has the parameter
                // "?generateThumbnaiIfNecessary=true" tacked onto the end
                // [See getTemplatePageImageSource()], whereas 'updatedThumbUrl' is just the url
                // to the updated thumbnail image.
                if (imgSrc.startsWith(updatedThumbUrl)) {
                    const newSrc = imgSrc + "?reload=" + internalCounter++;
                    // Force the image to be reloaded by replacing its src attribute with something different.
                    img.src = newSrc;
                }
            }
            setRedoCounter(internalCounter);
        },
        [redoCounter]
    );

    useEffect(() => {
        WebSocketManager.addListener("page-chooser", thumbnailUpdatedListener);
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
            // In reading:
            // https://stackoverflow.com/questions/56885037/react-batch-updates-for-multiple-setstate-calls-inside-useeffect-hook
            // I believe these 3 setState functions will be batched and performed at once.
            // DefaultPageId will usually still be undefined, unless we've already opened the dialog this run.
            setDefaultPageId(defaultPageId);
            setOrientation(initializationObject["orientation"]);
            setTemplateBookUrls(arrayOfBookUrls);
        });
        return () => {
            WebSocketManager.removeListener(
                "page-chooser",
                thumbnailUpdatedListener
            );
        };
    }, [thumbnailUpdatedListener]);

    const getToolId = (templatePageDiv: HTMLDivElement | undefined): string => {
        return templatePageDiv
            ? getAttributeStringSafely(templatePageDiv, "data-tool-id")
            : "";
    };

    // "Safely" from a type-checking point of view. The calling code is responsible
    // to make sure that empty string is handled.
    const getTextOfFirstElementByClassNameSafely = (
        element: Element,
        className: string
    ): string => {
        if (!element) {
            return "";
        }
        const queryResult = element.querySelector("." + className);
        if (!queryResult) {
            return "";
        }
        const text = (queryResult as Element).textContent;
        return text ? text : "";
    };

    const isDigitalOnly = (templatePageDiv: HTMLElement): boolean => {
        const classList = templatePageDiv.classList;
        return (
            classList.contains("bloom-nonprinting") &&
            !classList.contains("bloom-noreader")
        );
    };

    const learnMoreLink = selectedTemplatePageDiv
        ? getAttributeStringSafely(selectedTemplatePageDiv, "help-link")
        : "";

    const getPageDescription = (templatePageDiv: HTMLElement): string => {
        return getTextOfFirstElementByClassNameSafely(
            templatePageDiv,
            "pageDescription"
        );
    };

    const templatePageClickHandler = (
        selectedPageDiv: HTMLDivElement,
        selectedTemplateBookUrl: string
    ): void => {
        setSelectedTemplatePageDiv(selectedPageDiv);
        setSelectedTemplateBookPath(selectedTemplateBookUrl);
    };

    // Double-click handler should select a template page and then do the default action, if possible.
    const templatePageDoubleClickHandler = (
        selectedPageDiv: HTMLDivElement
    ): void => {
        // N.B. The double click handler in the inner component does the single-click actions first.
        const pageIsEnterpriseOnly = selectedPageDiv.classList.contains(
            "enterprise-only-flag"
        );
        if (pageIsEnterpriseOnly && !isEnterpriseAvailable) {
            return;
        }
        const convertAnywayCheckbox = document.getElementById(
            "convertAnywayCheckbox"
        ) as HTMLInputElement;
        const convertWholeBookCheckbox = document.getElementById(
            "convertWholeBookCheckbox"
        ) as HTMLInputElement;
        const bookPath = selectedTemplateBookPath
            ? selectedTemplateBookPath
            : "";
        handleAddPageOrChooseLayoutButtonClick(
            props.forChooseLayout,
            selectedPageDiv.id,
            bookPath,
            convertAnywayCheckbox ? convertAnywayCheckbox.checked : false,
            willLoseData(selectedPageDiv),
            convertWholeBookCheckbox ? convertWholeBookCheckbox.checked : false,
            props.forChooseLayout ? -1 : 1,
            getToolId(selectedPageDiv)
        );
    };

    const bookLoadedHandler = () => {
        const newCount = templateBooksLoadedCount + 1;
        if (newCount === templateBookUrls.length) {
            setTemplateBooksLoadedCount(0);
            setAllPagesLoaded(true);
        } else if (!allPagesLoaded) {
            setTemplateBooksLoadedCount(newCount);
        }
    };

    const getTemplateBooks = (): JSX.Element[] | undefined => {
        if (templateBookUrls.length === 0) {
            return undefined;
        }
        return templateBookUrls.map((groupData, index) => {
            return (
                <TemplateBookPages
                    selectedPageId={
                        selectedTemplatePageDiv
                            ? selectedTemplatePageDiv.id
                            : undefined
                    }
                    defaultPageIdToSelect={
                        selectedTemplatePageDiv ? undefined : defaultPageId
                    }
                    firstGroup={index === 0}
                    groupUrls={groupData}
                    orientation={orientation}
                    forChooseLayout={props.forChooseLayout}
                    key={index}
                    onTemplatePageSelect={templatePageClickHandler}
                    onTemplatePageDoubleClick={templatePageDoubleClickHandler}
                    onLoad={bookLoadedHandler}
                />
            );
        });
    };

    // It doesn't work to put this scrolling-on-selection mechanism in TemplateBookPages, because we need
    // the entire dialog (left half anyway) to render before we scroll, otherwise we may end up rendering
    // another group above the selected one and scrolling it off the screen again.
    useEffect(() => {
        if (!allPagesLoaded) return;
        const selectedNode = document.getElementsByClassName(
            "selectedTemplatePage"
        )[0];
        // If the original initializationObject defined the optional 'defaultPageToSelect', then it will be
        // loaded into 'defaultPageId' and that page selected on rendering it. Here we scroll to show it.
        // There's no point in scrolling on every select, since the user has to see a page to click on it.
        if (selectedNode && defaultPageId === selectedTemplatePageDiv?.id) {
            selectedNode.scrollIntoView({
                behavior: "smooth",
                block: "nearest"
            });
        }
        // We were having trouble with the scrolling to the last page added being triggered before some of
        // the earlier (in the list) template books had loaded their pages. Now we wait until all of the
        // template books have returned from their respective axios calls with their pages (or errored out).
        // This keeps the scrolling linked to the actual size of the dialog, since the groupDisplay section
        // of the dialog grows to accomodate all the pages as it's being rendered.
    }, [defaultPageId, selectedTemplatePageDiv, allPagesLoaded]);

    // Return true if choosing the current layout will cause loss of data
    const willLoseData = (
        templatePageDiv: HTMLDivElement | undefined
    ): boolean => {
        if (templatePageDiv === undefined) {
            return true;
        }
        const selectedTemplateTranslationGroupCount = countTranslationGroupsForChangeLayout(
            templatePageDiv
        );
        const selectedTemplatePictureCount = countEltsOfClassNotInImageContainer(
            templatePageDiv,
            "bloom-imageContainer"
        );
        const selectedTemplateVideoCount = countEltsOfClassNotInImageContainer(
            templatePageDiv,
            "bloom-videoContainer"
        );
        const selectedTemplateWidgetCount = countEltsOfClassNotInImageContainer(
            templatePageDiv,
            "bloom-widgetContainer"
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

    const getFolderForSelectedBook = (): string => {
        if (templateBookUrls.length === 0 || !selectedTemplateBookPath) {
            return ""; // paranoia; won't happen
        }
        return templateBookUrls.filter(
            group => group.templateBookPath === selectedTemplateBookPath
        )[0].templateBookFolderUrl;
    };

    const getPreviewImageSource = (): string => {
        if (!selectedTemplateBookPath || !selectedTemplatePageDiv) {
            return "";
        }
        const templateBookFolder = getFolderForSelectedBook();
        return getTemplatePageImageSource(
            templateBookFolder,
            getPageLabel(selectedTemplatePageDiv),
            orientation
        );
    };

    return (
        <BloomDialog
            open={open}
            onClose={() => {
                closeDialog();
            }}
            onCancel={() => {
                closeDialog();
            }}
            css={css`
                padding-left: 18px;
                padding-bottom: 20px;
                display: flex;
                background-color: unset; // BloomDialog default is somehow not what we want...
                .MuiDialog-paperWidthSm {
                    max-width: 925px;
                }
                .MuiDialog-paper {
                    margin: 0;
                    background-color: #f0f0f0;
                }
                #draggable-dialog-title {
                    padding-right: 12px;
                }
            `}
        >
            <DialogTitle title={dialogTitle} backgroundColor={"white"} />
            <DialogMiddle
                css={css`
                    border: none;
                    margin: 0;
                    min-height: 450px;
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
                        overflow-x: hidden;
                        height: auto;
                        flex: 2;
                        display: flex;
                        flex-direction: column;
                        max-width: 425px;
                    `}
                >
                    {templateBookUrls.length > 0 && getTemplateBooks()}
                </div>
                <div
                    css={css`
                        flex: 1;
                        height: auto;
                        // Setting min/max width here, allows the other pane to default to being wide
                        // enough for 3 columns with scroll bar.
                        min-width: 370px;
                        max-width: 420px;
                        display: flex;
                        flex-direction: row;
                        align-items: center;
                    `}
                >
                    {/* We will always select something. This only delays the preview display until we set a selection. */}
                    {selectedTemplatePageDiv && selectedTemplateBookPath && (
                        <SelectedTemplatePageControls
                            caption={getPageLabel(selectedTemplatePageDiv)}
                            pageDescription={getPageDescription(
                                selectedTemplatePageDiv
                            )}
                            pageId={selectedTemplatePageDiv.id}
                            imageSource={getPreviewImageSource()}
                            enterpriseAvailable={isEnterpriseAvailable}
                            pageIsEnterpriseOnly={selectedTemplatePageDiv.classList.contains(
                                "enterprise-only"
                            )}
                            pageIsDigitalOnly={isDigitalOnly(
                                selectedTemplatePageDiv
                            )}
                            templateBookPath={
                                selectedTemplateBookPath
                                    ? selectedTemplateBookPath
                                    : ""
                            }
                            isLandscape={orientation === "landscape"}
                            forChangeLayout={props.forChooseLayout}
                            willLoseData={
                                props.forChooseLayout
                                    ? willLoseData(selectedTemplatePageDiv)
                                    : false
                            }
                            learnMoreLink={learnMoreLink}
                            requiredTool={getToolId(selectedTemplatePageDiv)}
                            onSubmit={handleAddPageOrChooseLayoutButtonClick}
                        />
                    )}
                </div>
            </DialogMiddle>
        </BloomDialog>
    );
};

export function showPageChooserDialog(forChooseLayout: boolean) {
    ShowEditViewDialog(<PageChooserDialog forChooseLayout={forChooseLayout} />);
}

// Utility functions used by both PageChooserDialog and TemplateBookPages

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
