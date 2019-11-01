/// <reference path="../lib/localizationManager/localizationManager.ts" />
import * as React from "react";
import * as ReactDOM from "react-dom";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";
import axios from "axios";
import { BloomApi } from "../utils/bloomApi";
import "errorHandler";
import SelectedTemplatePageControls from "./selectedTemplatePageControls";
import { getEditViewFrameExports } from "../bookEdit/js/bloomFrames";
import { ThemeProvider } from "@material-ui/styles";
import theme from "../bloomMaterialUITheme";

document.addEventListener("DOMContentLoaded", () => {
    BloomApi.get("pageTemplates", result => {
        const templatesJSON = result.data;
        const pageChooser = new PageChooser(JSON.stringify(templatesJSON));
        pageChooser.loadPageGroups();
    });
});

interface IGroupData {
    templateBookFolderUrl: string;
    templateBookPath: string;
}

// latest version of the expected JSON initialization string (from PageTemplatesApi.HandleTemplatesRequest)
// "{\"defaultPageToSelect\":\"(guid of template page)\",
//   \"orientation\":\"landscape\",
//   \"groups\":[{\"templateBookFolderUrl\":\"/bloom/localhost/C$/BloomDesktop/DistFiles/factoryGroups/Templates/Basic Book\",
//                     \"templateBookUrl\":\"/bloom/localhost/C$/BloomDesktop/DistFiles/factoryGroups/Templates/Basic Book/Basic Book.htm\"}]}"

export class PageChooser {
    private _enterpriseAvailable: boolean;
    private _templateBookUrls: Array<IGroupData>;
    private _defaultPageToSelect: string;
    private _orientation: string;
    private _selectedGridItem: HTMLElement | undefined;
    private _indexOfPageToSelect: number;
    private _scrollingDiv: HTMLDivElement;
    private _forChooseLayout: boolean;

    constructor(initializationJsonString: string) {
        let initializationObject: object;
        if (initializationJsonString) {
            try {
                initializationObject = JSON.parse(initializationJsonString);
            } catch (e) {
                alert("Received bad JSON string: " + e);
                return;
            }
            this._templateBookUrls = initializationObject["groups"];
            this._defaultPageToSelect =
                initializationObject["defaultPageToSelect"];
            this._orientation = initializationObject["orientation"];
            this._forChooseLayout = initializationObject["forChooseLayout"];
        } else {
            alert("Expected url in PageChooser ctor!");
        }

        this._selectedGridItem = undefined;
        this._indexOfPageToSelect = 0;
        // I was hoping to confine this to 'selectedTemplatePageControls.tsx', but we need it for the double-click handler.
        BloomApi.get("common/enterpriseFeaturesEnabled", enterpriseResult => {
            this._enterpriseAvailable = enterpriseResult.data;
        });
    }

    // "Safely" from a type-checking point of view. The calling code is responsible
    // to make sure that empty string is handled.
    private getTextOfFirstElementByClassNameSafely(
        element: Element,
        className: string
    ): string {
        if (!element) {
            return "";
        }
        const queryResult = element.querySelector("." + className);
        if (!queryResult) {
            return "";
        }
        const text = (queryResult as Element).textContent;
        return text ? text : "";
    }

    private thumbnailClickHandler(clickedDiv: HTMLElement): void {
        // 'clickedDiv' is an .invisibleThumbCover
        // Select new thumbnail
        const newsel = clickedDiv.parentElement;
        if (newsel === null) return;
        // Mark any previously selected thumbnail as no longer selected
        if (this._selectedGridItem != undefined) {
            this._selectedGridItem.classList.remove("ui-selected");
        }
        this._selectedGridItem = newsel;
        this._selectedGridItem.classList.add("ui-selected");

        // Scroll to show it (useful for original selection). So far this only scrolls DOWN
        // to make sure we can see the BOTTOM of the clicked item; that's good enough for when
        // we open the dialog and a far-down item is selected, and marginally helpful when we click
        // an item partly scrolled off the bottom. There's no way currently to select an item
        // that's entirely scrolled off the top, and it doesn't seem worth the complication
        // to force a partly-visible one at the top to become wholly visible.
        const container = this._selectedGridItem.closest(".gridItemDisplay");
        // block: "nearest" is best, but "nearest" and "center" are not supported until FF v58
        // container.scrollIntoView({ behavior: "smooth", block: "nearest" });
        if (container) {
            container.scrollIntoView({ behavior: "smooth", block: "start" });
        }

        // Display large preview
        // This is now done with React via the TemplatePagePreview component.
        // Localization will happen there, so we just send english strings from the page templates.
        // 'gridItemCaption' and 'pageDescription' will exist... they may not have content.
        const englishCaptionText = this.getTextOfFirstElementByClassNameSafely(
            this._selectedGridItem,
            "gridItemCaption"
        );
        const englishPageDescription = this.getTextOfFirstElementByClassNameSafely(
            this._selectedGridItem,
            "pageDescription"
        );
        const isEnterprise = this._selectedGridItem.classList.contains(
            "enterprise-only-flag"
        );
        const imgSrc = this.getAttributeStringSafely(
            this._selectedGridItem.getElementsByTagName("img")[0],
            "src"
        );
        const previewElement = document.getElementById(
            "singlePagePreview"
        ) as HTMLDivElement;
        const groupElement = this._selectedGridItem.closest(".group");
        ReactDOM.render(
            React.createElement(
                ThemeProvider,
                { theme: theme },
                React.createElement(SelectedTemplatePageControls, {
                    enterpriseAvailable: this._enterpriseAvailable,
                    caption: englishCaptionText ? englishCaptionText : "",
                    imageSource: imgSrc,
                    pageDescription: englishPageDescription
                        ? englishPageDescription
                        : "",
                    pageIsDigitalOnly: this.isDigitalOnly(
                        this._selectedGridItem
                    ),
                    pageIsEnterpriseOnly: isEnterprise,
                    templateBookPath: this.getAttributeStringSafely(
                        groupElement,
                        "data-template-book-path"
                    ),
                    pageId: this.getAttributeStringSafely(
                        this._selectedGridItem,
                        "data-pageId"
                    ),
                    forChangeLayout: this._forChooseLayout,
                    willLoseData: this._forChooseLayout
                        ? this.willLoseData()
                        : false
                })
            ),
            previewElement
        );
    } // thumbnailClickHandler

    // "Safely" from a type-checking point of view. The calling code is responsible to make sure
    // that empty string is handled.
    private getAttributeStringSafely(
        element: Element | null,
        attributeName: string
    ): string {
        if (!element || !element.hasAttribute(attributeName)) {
            return "";
        }
        const value = element.getAttribute(attributeName);
        return value ? value : "";
    }

    // Return true if choosing the current layout will cause loss of data
    private willLoseData(): boolean {
        if (this._selectedGridItem === undefined) {
            return true;
        }
        const selectedTemplateTranslationGroupCount = parseInt(
            this.getAttributeStringSafely(
                this._selectedGridItem,
                "data-textDivCount"
            ),
            10
        );
        const selectedTemplatePictureCount = parseInt(
            this.getAttributeStringSafely(
                this._selectedGridItem,
                "data-pictureCount"
            ),
            10
        );
        const selectedTemplateVideoCount = parseInt(
            this.getAttributeStringSafely(
                this._selectedGridItem,
                "data-videoCount"
            ),
            10
        );

        const page = <HTMLIFrameElement>(
            window.parent.document.getElementById("page")
        );
        const current =
            page && page.contentWindow
                ? page.contentWindow.document.body
                : undefined;
        if (current === undefined) {
            return true;
        }
        const currentTranslationGroupCount = this.countTranslationGroupsForChangeLayout(
            current
        );
        const currentPictureCount = current.getElementsByClassName(
            "bloom-imageContainer"
        ).length;
        // ".bloom-videoContainer:not(.bloom-noVideoSelected)" is not working reliably as a selector.
        // It's also insufficient if we allow the user to change multiple pages at once to look at
        // only the current page for content.  Not checking for actual video content matches what is
        // done for text and pictures, and means that the check is equally valid for any number of
        // pages with the same layout.  See https://issues.bloomlibrary.org/youtrack/issue/BL-6921.
        const currentVideoCount = current.getElementsByClassName(
            "bloom-videoContainer"
        ).length;

        return (
            selectedTemplateTranslationGroupCount <
                currentTranslationGroupCount ||
            selectedTemplatePictureCount < currentPictureCount ||
            selectedTemplateVideoCount < currentVideoCount
        );
    }

    private isDigitalOnly(element: HTMLElement): boolean {
        const classList = element.classList;
        return (
            classList.contains("bloom-nonprinting") &&
            !classList.contains("bloom-noreader")
        );
    }

    // Set the text of the given element to the appropriate localization of defaultText
    // (or to defaultText, if no localization is available).
    // If defaultText is empty, set the element text to empty.
    // The localization ID to look up is made by concatenating the supplied prefix and the id
    // parameter, which defaults to the defaultText since we often use the English text of a
    // label as the last part of its ID.
    private setLocalizedText(
        elt: HTMLElement,
        idPrefix: string,
        defaultText: string,
        id: string = defaultText
    ) {
        defaultText = defaultText.trim();
        if (defaultText) {
            const comment = this.getAttributeStringSafely(elt, "l10nComment");
            theOneLocalizationManager
                .asyncGetText(idPrefix + id, defaultText, comment)
                .done(translation => {
                    elt.textContent = translation;
                });
        } else {
            elt.textContent = "";
        }
    }

    // This static method handles the button click in the Add Page or Change Layout dialog
    // It gets passed (through an exported function after this class) to the React component that displays
    // the page preview and deals with all the various checkbox logic.
    public static handleAddPageOrChooseLayoutButtonClick(
        forChangeLayout: boolean,
        pageId: string,
        templateBookPath: string,
        convertAnywayChecked: boolean,
        willLoseData: boolean,
        convertWholeBookChecked: boolean
    ): void {
        if (forChangeLayout) {
            if (willLoseData && !convertAnywayChecked) {
                return;
            }
            BloomApi.postData(
                "changeLayout",
                {
                    pageId: pageId,
                    templateBookPath: templateBookPath,
                    convertWholeBook: convertWholeBookChecked
                },
                PageChooser.closeup
            );
        } else {
            BloomApi.postData(
                "addPage",
                {
                    templateBookPath: templateBookPath,
                    pageId: pageId,
                    convertWholeBook: false
                },
                PageChooser.closeup
            );
        }
    }

    private static closeup(): void {
        BloomApi.postBoolean("editView/setModalState", false);
        // this fails with a message saying the dialog isn't initialized. Apparently a dialog must be closed
        // by code loaded into the window that opened it.
        //$(parent.document.getElementById('addPageConfig')).dialog('close');
        getEditViewFrameExports().closeDialog("addPageConfig");
    }

    // This is the starting-point method that is invoked to initialize the dialog.
    // At the point where it is called, the json parameters that control what will be displayed
    public loadPageGroups(): void {
        // Save a reference to the scrolling div that contains the various page items.
        this._scrollingDiv = document.querySelector(
            ".gridItemDisplay"
        ) as HTMLDivElement;

        // Save html sections that will get cloned later
        // there should only be one 'group' at this point; a stub with one default template page
        const groupHtml = (this._scrollingDiv.querySelector(
            ".group"
        ) as HTMLElement).cloneNode(true) as HTMLElement;
        // there should only be the one default 'gridItem' at this point
        const gridItemHtml = (groupHtml.querySelector(
            ".gridItem"
        ) as HTMLElement).cloneNode(true) as HTMLElement;
        if (this._templateBookUrls.length > 0) {
            // Remove original stub section
            this.emptyOutElementChildren(
                this._scrollingDiv.querySelector(".outerGroupContainer")
            );
            this.loadNextPageGroup(
                this._templateBookUrls,
                groupHtml,
                gridItemHtml,
                this._defaultPageToSelect,
                0
            );
        }

        if (this._orientation === "landscape") {
            const mainContainer = document.getElementById("mainContainer");
            if (mainContainer) {
                // just to satisfy compiler
                mainContainer.classList.add("landscape");
            }
        }
    } // loadPageGroups

    private emptyOutElementChildren(element: Element | null) {
        while (element && element.firstChild) {
            element.removeChild(element.firstChild);
        }
    }

    // This pops one template book order from the queue, does the async get,
    // loads it in the dialog, then recursively goes back for another.
    // Doing one at a time does two things for us. First, it makes the
    // books get added in the order we want (which we couldn't control if we ask for them all
    // at once). Secondly, it ensures we get the most important template pages shown and ready
    // to use as quickly as possible.
    private loadNextPageGroup(
        queue: IGroupData[],
        groupHTML: HTMLElement,
        gridItemHTML: HTMLElement,
        defaultPageToSelect: string,
        previousPagesCount: number
    ): void {
        const order = queue.shift();
        if (!order) {
            return; // no more to get
        }
        axios
            .get("/bloom/" + order.templateBookPath)
            .then(result => {
                const pageData: HTMLElement = new DOMParser().parseFromString(
                    result.data,
                    "text/html"
                ).body;

                const originalPages: HTMLElement[] = Array.from(
                    pageData.querySelectorAll(".bloom-page")
                );
                let pages = originalPages.filter(
                    (elem: HTMLElement) =>
                        elem.id && elem.getAttribute("data-page") === "extra"
                );
                if (pages.length == 0) {
                    console.log(
                        "Could not find any template pages in " +
                            order.templateBookPath
                    );
                    //don't add a group for books that don't have template pages; just move on.
                    // (This will always be true for a newly created template.)
                    this.loadNextPageGroup(
                        queue,
                        groupHTML,
                        gridItemHTML,
                        defaultPageToSelect,
                        previousPagesCount
                    );
                    return; // suppress adding this group.
                }

                const bookTitleElement = pageData.querySelector(
                    "div[data-book='bookTitle']"
                );
                let groupTitle = "";
                if (bookTitleElement) {
                    groupTitle = bookTitleElement.textContent
                        ? bookTitleElement.textContent
                        : "";
                }
                // Add title and container to dialog
                const groupToAdd = groupHTML.cloneNode(true) as HTMLElement;
                groupToAdd.setAttribute(
                    "data-template-book-path",
                    order.templateBookPath
                );
                const captionDiv = groupToAdd.querySelector(
                    ".groupCaption"
                ) as HTMLElement;
                this.setLocalizedText(
                    captionDiv,
                    "TemplateBooks.BookName.",
                    groupTitle
                );
                (this._scrollingDiv.querySelector(
                    ".outerGroupContainer"
                ) as HTMLElement).appendChild(groupToAdd);

                if (this._forChooseLayout) {
                    // This filters out the (empty) custom page, which is currently never a useful layout change,
                    // since all data would be lost.
                    pages = pages.filter(
                        (elem: HTMLElement) =>
                            elem.id != "5dcd48df-e9ab-4a07-afd4-6a24d0398386"
                    );
                }
                // console.log(
                //     "loadPageFromGroup(" + order.templateBookFolderUrl + ")"
                // );
                this.loadPageFromGroup(
                    groupToAdd,
                    pages,
                    gridItemHTML,
                    order.templateBookFolderUrl,
                    defaultPageToSelect,
                    previousPagesCount
                );
                const pagesCountSoFar = previousPagesCount + pages.length;

                this.loadNextPageGroup(
                    queue,
                    groupHTML,
                    gridItemHTML,
                    defaultPageToSelect,
                    pagesCountSoFar
                );
            })
            .catch(e => {
                //we don't really want to let one bad template keep us from showing others.
                // Insert a message into the dialog
                const path = order.templateBookPath;
                const index = path.lastIndexOf("/");
                const templateName = path.substring(index + 1, path.length);
                const templateTitle = templateName.replace(".html", "");
                const groupToAdd = groupHTML.cloneNode(true) as HTMLElement;
                this.setLocalizedText(
                    groupToAdd.querySelector(".groupCaption") as HTMLElement,
                    "TemplateBooks.BookName.",
                    templateTitle
                );
                const innerGroup = groupToAdd.querySelector(
                    ".innerGroupContainer"
                ) as HTMLElement;
                innerGroup.remove();
                groupToAdd.innerHTML = "<div id='missingMsg'/>";
                theOneLocalizationManager
                    .asyncGetText(
                        "EditTab.AddPageDialog.NoTemplate",
                        "Could not find {0}",
                        ""
                    )
                    .done(translation => {
                        const msgGroup = groupToAdd.querySelector(
                            "#missingMsg"
                        );
                        if (msgGroup === null) return; // safety net: shouldn't be able to occur
                        msgGroup.textContent = translation.replace(
                            "{0}",
                            templateName
                        );
                    });
                (this._scrollingDiv.querySelector(
                    ".outerGroupContainer"
                ) as HTMLElement).appendChild(groupToAdd);

                this.loadNextPageGroup(
                    queue,
                    groupHTML,
                    gridItemHTML,
                    defaultPageToSelect,
                    previousPagesCount
                );
            });
    }

    private loadPageFromGroup(
        currentGroup: HTMLElement,
        pageArray: Array<HTMLElement>,
        gridItemTemplate: HTMLElement,
        templateBookFolderUrl: string,
        defaultPageToSelect: string,
        previousPagesCount: number
    ): void {
        if (pageArray.length < 1) {
            console.log("pageArray empty for " + templateBookFolderUrl);
            return;
        }

        // Remove default template page
        (currentGroup.querySelector(".gridItem") as HTMLElement).remove();
        let gotSelectedPage = false;
        // insert a template page for each page with the correct #id on the url
        pageArray.forEach((currentPageDiv: HTMLElement, index) => {
            if (currentPageDiv.getAttribute("data-page") === "singleton")
                return; // skip this one

            const currentGridItemHtml = gridItemTemplate.cloneNode(
                true
            ) as HTMLElement;

            if (currentPageDiv.classList.contains("enterprise-only")) {
                currentGridItemHtml.classList.add("enterprise-only-flag");
            }
            const currentId = currentPageDiv.id;
            currentGridItemHtml.setAttribute("data-pageId", currentId);
            currentGridItemHtml.setAttribute(
                "data-textDivCount",
                this.countTranslationGroupsForChangeLayout(
                    currentPageDiv
                ).toString()
            );
            currentGridItemHtml.setAttribute(
                "data-pictureCount",
                currentPageDiv
                    .getElementsByClassName("bloom-imageContainer")
                    .length.toString()
            );
            currentGridItemHtml.setAttribute(
                "data-videoCount",
                currentPageDiv
                    .getElementsByClassName("bloom-videoContainer")
                    .length.toString()
            );

            // The check for _indexOfPageToSelect here keeps the selection on the *first* matching page. In BL-4500, we found
            // that different templates could reuse the same guid for custom page. That's a problem probably should be
            // sorted out, but it's out "in the wild" in the Story Primer, so we have to have a fix that doesn't depend
            // on what templates the user has installed.
            if (
                currentId === defaultPageToSelect &&
                this._indexOfPageToSelect === 0
            ) {
                this._indexOfPageToSelect = index + previousPagesCount;
                gotSelectedPage = true;
            }

            const pageDescription = this.getTextOfFirstElementByClassNameSafely(
                currentPageDiv,
                "pageDescription"
            );
            (currentGridItemHtml.querySelector(
                ".pageDescription"
            ) as HTMLElement).textContent = pageDescription;

            // We can use these classes to determine how to display the preview.
            // Currently, this is used to determine if a template page is digital-only
            // (meaning not for PDF publication) which results in a message to the user.
            // See the classes referenced in isDigitalOnly().
            Array.from(currentPageDiv.classList).forEach(cssClass => {
                if (cssClass.startsWith("bloom-"))
                    currentGridItemHtml.classList.add(cssClass);
            });

            const rawPageLabel = this.getTextOfFirstElementByClassNameSafely(
                currentPageDiv,
                "pageLabel"
            );
            let pageLabel = "";
            if (rawPageLabel) {
                pageLabel = rawPageLabel.trim();
                (currentGridItemHtml.querySelector(
                    ".gridItemCaption"
                ) as HTMLElement).textContent = pageLabel;
            }
            const possibleImageUrl = this.getPossibleImageUrl(
                templateBookFolderUrl,
                pageLabel
            );
            currentGridItemHtml
                .getElementsByTagName("img")[0]
                .setAttribute("src", possibleImageUrl);

            (currentGroup.querySelector(
                ".innerGroupContainer"
            ) as HTMLElement).appendChild(currentGridItemHtml);
        }); // each
        // once the template pages are installed, attach click handler to them.
        const thumbCovers = currentGroup.querySelectorAll(
            ".invisibleThumbCover"
        );
        Array.from(thumbCovers).forEach((thumbCover: HTMLElement) => {
            thumbCover.addEventListener("dblclick", () => {
                if (!this._selectedGridItem || !this._templateBookUrls) {
                    return;
                }
                const pageIsEnterpriseOnly = this._selectedGridItem.classList.contains(
                    "enterprise-only-flag"
                );
                if (pageIsEnterpriseOnly && !this._enterpriseAvailable) {
                    return;
                }
                const convertAnywayCheckbox = document.getElementById(
                    "convertAnywayCheckbox"
                ) as HTMLInputElement;
                const convertWholeBookCheckbox = document.getElementById(
                    "convertWholeBookCheckbox"
                ) as HTMLInputElement;
                const closestGroupAncestor = this._selectedGridItem.closest(
                    ".group"
                );
                const bookPath = this.getAttributeStringSafely(
                    closestGroupAncestor,
                    "data-template-book-path"
                );
                const pageId = this.getAttributeStringSafely(
                    this._selectedGridItem,
                    "data-pageId"
                );
                PageChooser.handleAddPageOrChooseLayoutButtonClick(
                    this._forChooseLayout,
                    pageId,
                    bookPath,
                    convertAnywayCheckbox
                        ? convertAnywayCheckbox.checked
                        : false,
                    this.willLoseData(),
                    convertWholeBookCheckbox
                        ? convertWholeBookCheckbox.checked
                        : false
                );
            }); // invisibleThumbCover double click handler

            thumbCover.addEventListener("click", () =>
                this.thumbnailClickHandler(thumbCover)
            ); // invisibleThumbCover click
        }); // forEach
        // If we found the specified page to select in this group, it is the one indicated by
        // this._indexOfPageToSelect; select that now.
        // In case we were not provided with a default page to select, this._indexOfPageToSelect remains 0,
        // and if this is the first group we go ahead and select its first page.
        if (
            gotSelectedPage ||
            (defaultPageToSelect === "" && previousPagesCount == 0)
        ) {
            // thumbCovers contains the thumbnails for only for the current template, so the index
            // must be adjusted.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7472.
            this.thumbnailClickHandler(thumbCovers[
                this._indexOfPageToSelect - previousPagesCount
            ] as HTMLElement);
        }
    } // loadPageFromGroup

    // We want to count all the translationGroups that do not occur inside of a bloom-imageContainer div.
    // The reason for this is that images can have textOverPicture divs and imageDescription divs inside of them
    // and these are completely independent of the template page. We need to count regular translationGroups and
    // also ensure that translationGroups inside of images get migrated correctly. If this algorithm changes, be
    // sure to also change 'GetTranslationGroupsInternal()' in HtmlDom.cs.
    private countTranslationGroupsForChangeLayout(
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

    private getPossibleImageUrl(
        templateBookFolderUrl: string,
        pageLabel: string
    ): string {
        const label = pageLabel.replace("&", "+"); //ampersands confuse the url system
        // The result may actually be a png file or an svg, and there may be some delay while the png is generated.

        //NB:  without the generateThumbnaiIfNecessary=true, we can run out of worker threads and get deadlocked.
        //See EnhancedImageServer.IsRecursiveRequestContext
        return (
            "/bloom/api/pageTemplateThumbnail/" +
            templateBookFolderUrl +
            "/template/" +
            label +
            (this._orientation === "landscape" ? "-landscape" : "") +
            ".svg?generateThumbnaiIfNecessary=true"
        );
    }
} // End OF PageChooserClass

export function handleAddPageOrChooseLayoutButtonClick(
    forChangeLayout: boolean,
    pageId: string,
    templateBookPath: string,
    convertAnywayChecked: boolean,
    willLoseData: boolean,
    convertWholeBookChecked: boolean
) {
    PageChooser.handleAddPageOrChooseLayoutButtonClick(
        forChangeLayout,
        pageId,
        templateBookPath,
        convertAnywayChecked,
        willLoseData,
        convertWholeBookChecked
    );
}
