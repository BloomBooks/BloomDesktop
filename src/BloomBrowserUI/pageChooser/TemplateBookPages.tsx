/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";
import axios from "axios";
import { getBloomApiPrefix } from "../utils/bloomApi";
import Typography from "@mui/material/Typography";
import {
    getPageLabel,
    getTemplatePageImageSource,
    IGroupData
} from "./PageChooserDialog";
import PageThumbnail from "./PageThumbnail";
import TemplateBookErrorReplacement from "./TemplateBookErrorReplacement";
import { kBloomBlue50Transparent, kBloomPurple } from "../bloomMaterialUITheme";

interface ITemplateBookPagesProps {
    // If defined, either the default selection, or a user selection has been made. Highlight this page
    // when you come across it.
    selectedPageId?: string;
    // If defined, this means 'if this id shows up in my group, fire onTemplatePageSelect with it'.
    defaultPageIdToSelect?: string;
    // If neither of the 2 above props are defined and this is the first group, fire onTemplatePageSelect
    // on my first page.
    firstGroup: boolean;
    groupUrls: IGroupData;
    orientation: string;
    forChooseLayout: boolean;
    onTemplatePageSelect: (
        selectedPageDiv: HTMLDivElement,
        selectedTemplateBookUrl: string
    ) => void;
    onTemplatePageDoubleClick: (selectedPageDiv: HTMLDivElement) => void;
}

const transparentHighlightColor = kBloomBlue50Transparent;

// Unicode bullet character used to mark pages that are only available to enterprise users.
const enterpriseMarkerChar = "\u25cf";

export const TemplateBookPages: React.FunctionComponent<ITemplateBookPagesProps> = props => {
    const [pageData, setPageData] = useState<HTMLDivElement[] | undefined>(
        undefined
    );
    const [groupTitle, setGroupTitle] = useState("");
    const [errorState, setErrorState] = useState(false);

    const isLandscape = props.orientation === "landscape";

    // Here we grab the actual html file contents so we can process what's
    // available to us in terms of template pages and the content of those pages (especially for the
    // "change layout" functionality).
    // Theoretically a lot more of the processing of template books and their pages could be done on
    // the C# side of things. Then we could just pass a JSON array (perhaps) of template page data.
    // I think that's beyond the scope of what I'm trying to do right now, but it could be tackled later.
    useEffect(() => {
        axios
            .get(
                getBloomApiPrefix(false) +
                    encodeURIComponent(props.groupUrls.templateBookPath)
            )
            .then(result => {
                const pageData: HTMLElement = new DOMParser().parseFromString(
                    result.data,
                    "text/html"
                ).body;
                let bloomPages: HTMLDivElement[] = Array.from(
                    pageData.querySelectorAll(".bloom-page")
                );
                if (props.forChooseLayout) {
                    // This filters out the (empty) custom page, which is currently never a useful layout change,
                    // since all data would be lost.
                    bloomPages = bloomPages.filter(
                        elem =>
                            elem.id != "5dcd48df-e9ab-4a07-afd4-6a24d0398386"
                    );
                }
                // By previous usage, 'extra' pages are ones we can add (multiples of) to a book.
                // I'm not entirely clear why we call them 'extra', I think the term pre-dates me.
                // This filter by it's very nature eliminates pages with 'data-page' = 'singleton'.
                const filteredBloomPages = bloomPages.filter(
                    elem =>
                        elem.id && elem.getAttribute("data-page") === "extra"
                );

                // Don't add a group for books that don't have template pages; just move on.
                // (This will always be true for a newly created template.)
                if (filteredBloomPages.length === 0) {
                    console.log(
                        "Could not find any template pages in " +
                            props.groupUrls.templateBookPath
                    );
                    return;
                }

                const bookTitleElement = pageData.querySelector(
                    "div[data-book='bookTitle']"
                );
                if (bookTitleElement) {
                    setGroupTitle(
                        bookTitleElement.textContent
                            ? bookTitleElement.textContent.trim()
                            : ""
                    );
                }
                setPageData(filteredBloomPages);
            })
            .catch(reason => {
                console.log(reason);
                // We couldn't load a template file that the JSON says should be there.
                // Just display a message.
                setErrorState(true);
            });
    }, [props.forChooseLayout, props.groupUrls]);

    const pages = pageData
        ? pageData.map((currentPageDiv: HTMLDivElement, index) => {
              // Process each page and create a pageThumbnail div containing a page thumbnail image and data
              // and a clickable overlay.
              const pageIsEnterpriseOnly = currentPageDiv.classList.contains(
                  "enterprise-only"
              );

              const thisPageIsSelected =
                  currentPageDiv.id === props.selectedPageId;

              const enterpriseOnlyRules = pageIsEnterpriseOnly
                  ? `:after {
                        content: "${enterpriseMarkerChar}";
                        cursor: default;
                        color: ${kBloomPurple};
                        position: absolute;
                        top: -10px;
                        right: ${isLandscape ? "-4" : "8"}px;
                        font-size: 24px
                    }`
                  : "";
              const backgroundCss = thisPageIsSelected
                  ? `background: ${transparentHighlightColor};`
                  : "";

              return (
                  <div
                      key={index}
                      // The main dialog may scroll the page groups to this page initially, if
                      // defaultPageIdToSelect is set.
                      className={
                          thisPageIsSelected ? "selectedTemplatePage" : ""
                      }
                      css={css`
                          margin: 0 10px 10px 11px;
                          width: 104px;
                          display: inline-block;
                          // The absolutely positioned blue overlay child needs to be placed relative to
                          // this, not an ancestor. This keeps them together during scrolling, etc.
                          position: relative;
                          ${enterpriseOnlyRules}
                      `}
                  >
                      {/* A selection overlay that covers the actual thumbnail image. */}
                      <div
                          css={css`
                              z-index: 3;
                              position: absolute;
                              height: ${isLandscape ? "70px" : "100px"};
                              margin-left: 10px;
                              ${backgroundCss}
                              width: 100px;
                              :hover {
                                  background: ${transparentHighlightColor};
                              }
                          `}
                          onClick={() =>
                              templatePageClickHandler(currentPageDiv)
                          }
                          onDoubleClick={() =>
                              templatePageDoubleClickHandler(currentPageDiv)
                          }
                      />
                      <PageThumbnail
                          imageSource={getTemplatePageImageSource(
                              props.groupUrls.templateBookFolderUrl,
                              getPageLabel(currentPageDiv),
                              props.orientation
                          )}
                          isLandscape={isLandscape}
                      />
                  </div>
              );
          })
        : undefined;

    const templatePageClickHandler = (currentPageDiv: HTMLDivElement) => {
        props.onTemplatePageSelect(
            currentPageDiv,
            props.groupUrls.templateBookPath
        );
    };

    const templatePageDoubleClickHandler = (currentPageDiv: HTMLDivElement) => {
        templatePageClickHandler(currentPageDiv); // Do the default click action too.
        props.onTemplatePageDoubleClick(currentPageDiv);
    };

    // Fire off the selection mechanism if we don't already have a selection,
    // and the page that needs selecting is in this group.
    useEffect(() => {
        if (pageData && !props.selectedPageId) {
            if (props.defaultPageIdToSelect) {
                const matchingPageDivs = pageData.filter(
                    p => p.id === props.defaultPageIdToSelect
                );
                if (matchingPageDivs.length > 0) {
                    templatePageClickHandler(matchingPageDivs[0]);
                }
            } else {
                if (props.firstGroup) {
                    templatePageClickHandler(pageData[0]);
                }
            }
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [pageData]);

    return (
        <React.Fragment>
            {pages && (
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                    `}
                >
                    <Typography
                        variant="h6"
                        css={css`
                            margin-left: 20px !important;
                            padding-left: 4px !important;
                            font-weight: bold !important;
                            display: block;
                        `}
                    >
                        {groupTitle}
                    </Typography>
                    <div
                        className="templateBookGroup"
                        css={css`
                            margin: 0;
                        `}
                    >
                        {pages}
                    </div>
                </div>
            )}
            {errorState && (
                <TemplateBookErrorReplacement
                    templateBookPath={props.groupUrls.templateBookPath}
                />
            )}
        </React.Fragment>
    );
};

export default TemplateBookPages;
