/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";
import axios from "axios";
import PageThumbnail from "./PageThumbnail";
import { getBloomApiPrefix } from "../utils/bloomApi";
import { Typography } from "@mui/material";
import {
    countEltsOfClassNotInImageContainer,
    countTranslationGroupsForChangeLayout,
    getAttributeStringSafely,
    IGroupData
} from "./PageChooserDialog";
import { kBloomBlue50Transparent, kBloomPurple } from "../bloomMaterialUITheme";
import { ErrorGroup } from "./ErrorGroup";

interface IChooserPageGroupProps {
    groupUrls: IGroupData;
    orientation: string;
    forChooseLayout: boolean;
    onThumbSelect?: React.MouseEventHandler<HTMLDivElement>;
    onThumbDoubleClick?: React.MouseEventHandler<HTMLDivElement>;
}

const transparentHighlightColor = kBloomBlue50Transparent;

// Only exported for use by Storybook.
export const getThumbImageSource = (
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

export const ChooserPageGroup: React.FunctionComponent<IChooserPageGroupProps> = props => {
    const [pageData, setPageData] = useState<HTMLElement[] | undefined>(
        undefined
    );
    const [groupTitle, setGroupTitle] = useState("");
    const [errorState, setErrorState] = useState(false);
    const isLandscape = props.orientation === "landscape";

    useEffect(() => {
        axios
            .get(
                getBloomApiPrefix(false) +
                    encodeURIComponent(props.groupUrls.templateBookPath)
            )
            .then(result => {
                const pageData = new DOMParser().parseFromString(
                    result.data,
                    "text/html"
                ).body;
                let originalPages: HTMLElement[] = Array.from(
                    pageData.querySelectorAll(".bloom-page")
                );
                if (props.forChooseLayout) {
                    // This filters out the (empty) custom page, which is currently never a useful layout change,
                    // since all data would be lost.
                    originalPages = originalPages.filter(
                        (elem: HTMLElement) =>
                            elem.id != "5dcd48df-e9ab-4a07-afd4-6a24d0398386"
                    );
                }

                // Don't add a group for books that don't have template pages; just move on.
                // (This will always be true for a newly created template.)
                if (originalPages.length === 0) {
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
                setPageData(
                    originalPages.filter(
                        (elem: HTMLElement) =>
                            elem.id &&
                            elem.getAttribute("data-page") === "extra"
                    )
                );
            })
            .catch(reason => {
                console.log(reason);
                // We couldn't load a template file that the JSON says should be there.
                // Just display a message.
                setErrorState(true);
            });
    }, [props.forChooseLayout, props.groupUrls]);

    const getPageLabel = (page: HTMLElement): string => {
        const labelList = page.getElementsByClassName("pageLabel");
        return labelList.length > 0
            ? (labelList[0] as HTMLElement).innerText.trim()
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

    const isDigitalOnly = (pageDiv: HTMLElement): boolean => {
        const classList = pageDiv.classList;
        return (
            classList.contains("bloom-nonprinting") &&
            !classList.contains("bloom-noreader")
        );
    };

    const pages = pageData
        ? pageData.map((currentPageDiv: HTMLElement, index) => {
              // Process each page and create a gridItem div containing a thumb and data and an overlay.
              if (currentPageDiv.getAttribute("data-page") === "singleton")
                  return; // skip this one

              const pageLabel = getPageLabel(currentPageDiv);
              const isEnterprise = currentPageDiv.classList.contains(
                  "enterprise-only"
              );
              const translationGroupCount = countTranslationGroupsForChangeLayout(
                  currentPageDiv
              );
              const pageDescription = getTextOfFirstElementByClassNameSafely(
                  currentPageDiv,
                  "pageDescription"
              );
              const pictureCount = countEltsOfClassNotInImageContainer(
                  currentPageDiv,
                  "bloom-imageContainer"
              );
              const videoCount = countEltsOfClassNotInImageContainer(
                  currentPageDiv,
                  "bloom-videoContainer"
              );
              const widgetCount = countEltsOfClassNotInImageContainer(
                  currentPageDiv,
                  "bloom-widgetContainer"
              );
              const tool = getAttributeStringSafely(
                  currentPageDiv,
                  "data-tool-id"
              );
              const helpLink = getAttributeStringSafely(
                  currentPageDiv,
                  "help-link"
              );

              const enterpriseOnlyRules = isEnterprise
                  ? `:after {
                        content: "\u25cf";
                        cursor: default;
                        color: ${kBloomPurple};
                        position: absolute;
                        top: -10px;
                        right: ${isLandscape ? "-4" : "8"}px;
                        font-size: 24px
                    }`
                  : "";

              return (
                  <div
                      className="gridItem"
                      key={index}
                      // We keep a bunch of data about the page here, so it's available if or when
                      // this page gets selected. React gives a warning if the attribute name contains
                      // a capital letter, so we only use lowercase on the 'data-x' attributes.
                      data-page-id={currentPageDiv.id}
                      data-is-enterprise={isEnterprise}
                      data-text-div-count={translationGroupCount}
                      data-picture-count={pictureCount}
                      data-video-count={videoCount}
                      data-widget-count={widgetCount}
                      data-page-description={pageDescription}
                      data-page-label={pageLabel}
                      data-digital-only={isDigitalOnly(currentPageDiv)}
                      data-tool-id={tool}
                      data-help-link={helpLink}
                      css={css`
                          margin: 0 10px 10px 11px;
                          width: 104px;
                          display: inline-block;
                          position: sticky; //this makes the blue overlay, which is a child, be correctly positioned even when scrolled
                          .ui-selected {
                              background: ${transparentHighlightColor};
                          }
                          ${enterpriseOnlyRules}
                      `}
                  >
                      {/* A selection overlay that covers the actual thumbnail. */}
                      <div
                          css={css`
                              z-index: 3;
                              position: absolute;
                              height: ${isLandscape ? "70px" : "100px"};
                              margin-left: 10px;
                              width: 100px;
                              :hover {
                                  background: ${transparentHighlightColor};
                              }
                          `}
                          onClick={props.onThumbSelect}
                          onDoubleClick={props.onThumbDoubleClick}
                      />
                      <PageThumbnail
                          imageSource={getThumbImageSource(
                              props.groupUrls.templateBookFolderUrl,
                              pageLabel,
                              props.orientation
                          )}
                          isLandscape={isLandscape}
                      />
                  </div>
              );
          })
        : undefined;

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
                        className="gridGroup"
                        css={css`
                            margin: 0;
                        `}
                        data-template-book={props.groupUrls.templateBookPath}
                    >
                        {pages}
                    </div>
                </div>
            )}
            {errorState && (
                <ErrorGroup
                    templateBookPath={props.groupUrls.templateBookPath}
                />
            )}
        </React.Fragment>
    );
};
