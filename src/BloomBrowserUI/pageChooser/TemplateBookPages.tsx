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
    IBookGroup,
    ITemplateBookInfo
} from "./PageChooserDialog";
import PageThumbnail from "./PageThumbnail";
import TemplateBookErrorReplacement from "./TemplateBookErrorReplacement";
import { kBloomBlue50Transparent, kBloomPurple } from "../bloomMaterialUITheme";
import { Span } from "../react_components/l10nComponents";

export interface ITemplateBookPagesProps {
    // If defined, either the default selection, or a user selection has been made. Highlight this page
    // when you come across it.
    selectedPageId?: string;
    // If defined, this means 'if this id shows up in my group, fire onTemplatePageSelect with it'.
    defaultPageIdToSelect?: string;
    // If neither of the 2 above props are defined and this is the first group, fire onTemplatePageSelect
    // on my first page.
    firstGroup: boolean;
    titleGroup: IBookGroup;
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

export const TemplateBookPages: React.FunctionComponent<ITemplateBookPagesProps> = ({
    forChooseLayout,
    titleGroup,
    orientation,
    selectedPageId,
    defaultPageIdToSelect,
    firstGroup,
    onTemplatePageSelect,
    onTemplatePageDoubleClick
}) => {
    const [pageData, setPageData] = useState<HTMLDivElement[] | undefined>(
        undefined
    );
    const groupTitle = titleGroup.title;

    const isLandscape = orientation === "landscape";

    useEffect(() => {
        let bloomPages = titleGroup.books
            .map(book => book.pages)
            .flat() as HTMLDivElement[];
        if (forChooseLayout) {
            // This filters out the (empty) custom page, which is currently never a useful layout change,
            // since all data would be lost.
            bloomPages = bloomPages.filter(
                elem => elem.id != "5dcd48df-e9ab-4a07-afd4-6a24d0398386"
            );
        }
        // By previous usage, 'extra' pages are ones we can add (multiples of) to a book.
        // I'm not entirely clear why we call them 'extra', I think the term pre-dates me.
        // This filter by it's very nature eliminates pages with 'data-page' = 'singleton'.
        const filteredBloomPages = bloomPages.filter(
            elem => elem.id && elem.getAttribute("data-page") === "extra"
        );

        // Don't add a group for books that don't have template pages; just move on.
        // (This will always be true for a newly created template.)
        if (filteredBloomPages.length === 0) {
            console.log(
                "Could not find any template pages in " + titleGroup.title
            );
            return;
        }

        setPageData(filteredBloomPages);
    }, [forChooseLayout]);

    const getBookForPageDiv = (pageDiv: HTMLDivElement) => {
        return titleGroup.books.find(b => b.pages!.indexOf(pageDiv) >= 0);
    };

    const pages = pageData
        ? pageData.map((currentPageDiv: HTMLDivElement, index) => {
              // Process each page and create a pageThumbnail div containing a page thumbnail image and data
              // and a clickable overlay.
              const pageIsEnterpriseOnly = currentPageDiv.classList.contains(
                  "enterprise-only"
              );

              const thisPageIsSelected = currentPageDiv.id === selectedPageId;

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
                              getBookForPageDiv(currentPageDiv)!.url,
                              getPageLabel(currentPageDiv),
                              orientation
                          )}
                          isLandscape={isLandscape}
                      />
                  </div>
              );
          })
        : undefined;

    const templatePageClickHandler = (currentPageDiv: HTMLDivElement) => {
        onTemplatePageSelect(
            currentPageDiv,
            getBookForPageDiv(currentPageDiv)!.path
        );
    };

    const templatePageDoubleClickHandler = (currentPageDiv: HTMLDivElement) => {
        templatePageClickHandler(currentPageDiv); // Do the default click action too.
        onTemplatePageDoubleClick(currentPageDiv);
    };

    // Fire off the selection mechanism if we don't already have a selection,
    // and the page that needs selecting is in this group.
    useEffect(() => {
        if (pageData && !selectedPageId) {
            if (defaultPageIdToSelect) {
                const matchingPageDivs = pageData.filter(
                    p => p.id === defaultPageIdToSelect
                );
                if (matchingPageDivs.length > 0) {
                    templatePageClickHandler(matchingPageDivs[0]);
                }
            } else {
                if (firstGroup) {
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
                        <Span
                            contentEditable={false}
                            l10nKey={"TemplateBooks.BookName." + groupTitle}
                        >
                            {groupTitle}
                        </Span>
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
            {titleGroup.errorPath && (
                <TemplateBookErrorReplacement
                    templateBookPath={titleGroup.errorPath}
                />
            )}
        </React.Fragment>
    );
};

export default TemplateBookPages;
