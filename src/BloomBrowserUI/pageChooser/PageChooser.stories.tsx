/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import { lightTheme } from "../bloomMaterialUITheme";
import * as React from "react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { StorybookContext } from "../.storybook/StoryBookContext";
import { SelectedTemplatePageControls } from "./selectedTemplatePageControls";
import TemplateBookPages from "./TemplateBookPages";
import { ITemplateBookPagesProps } from "./TemplateBookPages";
import PageThumbnail from "./PageThumbnail";
import {
    getTemplatePageImageSource,
    IBookGroup,
    ITemplateBookInfo
} from "./PageChooserDialog";
import { TemplateBookErrorReplacement } from "./TemplateBookErrorReplacement";
import { getBloomApiPrefix } from "../utils/bloomApi";
import axios from "axios";
interface ITemplateBookPagesWrapperProps extends ITemplateBookPagesProps {
    templateBook: ITemplateBookInfo;
}

const dummyTitleGroup: IBookGroup = {
    title: "Dummy Title",
    books: [],
    errorPath: ""
};

const TemplateBookPagesWrapper: React.FunctionComponent<ITemplateBookPagesWrapperProps> = props => {
    const [bg, setBg] = React.useState<IBookGroup | undefined>(undefined);
    React.useEffect(() => {
        axios
            .get(
                getBloomApiPrefix(false) +
                    encodeURIComponent(props.templateBook.templateBookPath)
            )
            .then(result => {
                const resultPageData: HTMLElement = new DOMParser().parseFromString(
                    result.data,
                    "text/html"
                ).body;
                const bloomPages: HTMLDivElement[] = Array.from(
                    resultPageData.querySelectorAll(".bloom-page")
                );
                // do we need to filter for test purposes?
                const bg: IBookGroup = {
                    title: "Test Title",
                    books: [
                        {
                            path: props.templateBook.templateBookPath,
                            url: props.templateBook.templateBookFolderUrl,
                            dom: resultPageData,
                            pages: bloomPages,
                            id: "",
                            pageToolId: ""
                        }
                    ],
                    errorPath: ""
                };
                setBg(bg);
            })
            .catch(error => {
                alert(
                    "Error loading template book: " +
                        props.templateBook.templateBookPath
                );
            });
    }, [props.templateBook]);
    if (!bg) {
        return null;
    }
    return <TemplateBookPages {...props} titleGroup={bg!} />;
};

const PreviewFrame: React.FC = props => (
    <div
        css={css`
            height: 540px;
            display: flex;
        `}
    >
        <div
            css={css`
                color: blue;
                order: 2;
                max-width: 100px;
                border: 1px solid blue;
            `}
        >
            Don't forget that Bloom needs to be running for the api to work.
        </div>
        <div
            css={css`
                height: 98%;
                width: 420px;
                border: 1px solid blue;
                box-sizing: border-box;
                display: flex;
            `}
        >
            {props.children}
        </div>
    </div>
);

export default {
    title: "Page Chooser"
};

export const NormalPreview = () => {
    const templateFolderUrl =
        "c:\\github\\bloomdesktop\\output\\browser\\templates\\template books\\basic book";
    const pageLabel = "Picture on Left";
    const orientation = "portrait";
    return (
        <PreviewFrame>
            <SelectedTemplatePageControls
                caption={pageLabel}
                pageDescription="A text box on top of an image"
                imageSource={getTemplatePageImageSource(
                    templateFolderUrl,
                    pageLabel,
                    orientation
                )}
                pageIsDigitalOnly={false}
                pageId="1235"
                templateBookPath="somePath"
                isLandscape={false}
                dataToolId=""
            ></SelectedTemplatePageControls>
        </PreviewFrame>
    );
};

NormalPreview.story = {
    name: "normal preview"
};

export const PreviewRequiresEnterprise = () => {
    const templateFolderUrl =
        "c:\\github\\bloomdesktop\\output\\browser\\templates\\template books\\activity";
    const pageLabel = "Choose Picture from Word";
    const orientation = "landscape";
    return (
        <PreviewFrame>
            <SelectedTemplatePageControls
                caption={pageLabel}
                pageDescription="Some page description for a page that needs Enterprise"
                imageSource={getTemplatePageImageSource(
                    templateFolderUrl,
                    pageLabel,
                    orientation
                )}
                featureName="foobar"
                pageIsDigitalOnly={true}
                pageId="1235"
                templateBookPath="somePath"
                learnMoreLink="some-strange-link"
                isLandscape={true}
                dataToolId=""
            ></SelectedTemplatePageControls>
        </PreviewFrame>
    );
};

PreviewRequiresEnterprise.story = {
    name: "preview requiresEnterprise"
};

export const PreviewChangeLayoutWillLoseData = () => {
    const templateFolderUrl =
        "c:\\github\\bloomdesktop\\output\\browser\\templates\\template books\\basic book";
    const pageLabel = "Just Text";
    const orientation = "landscape";

    return (
        <PreviewFrame>
            <SelectedTemplatePageControls
                imageSource={getTemplatePageImageSource(
                    templateFolderUrl,
                    pageLabel,
                    orientation
                )}
                caption="Just Text"
                pageDescription="This page has space for only text. But I want to put a bigger description in here to make sure."
                featureName="foobar"
                pageIsDigitalOnly={false}
                pageId="1235"
                templateBookPath="somePath"
                forChangeLayout={true}
                willLoseData={true}
                isLandscape={true}
                dataToolId=""
            ></SelectedTemplatePageControls>
        </PreviewFrame>
    );
};

PreviewChangeLayoutWillLoseData.story = {
    name: "preview changeLayoutWillLoseData"
};

export const _TemplateBookPages = () => {
    const book: ITemplateBookInfo = {
        templateBookFolderUrl:
            "c:/bloomdesktop/output/browser/templates/template books/basic book",
        templateBookPath:
            "c:/bloomdesktop/output/browser/templates/template books/basic book/basic book.html"
    };
    return (
        <PreviewFrame>
            <TemplateBookPagesWrapper
                firstGroup={false}
                templateBook={book}
                titleGroup={dummyTitleGroup}
                orientation="portrait"
                forChooseLayout={false}
                onTemplatePageSelect={() => {}}
                onTemplatePageDoubleClick={() => {}}
            />
        </PreviewFrame>
    );
};

_TemplateBookPages.story = {
    name: "TemplateBookPages"
};

export const TemplateBookPagesActivityPortrait = () => {
    const book: ITemplateBookInfo = {
        templateBookFolderUrl:
            "c:/bloomdesktop/output/browser/templates/template books/activity",
        templateBookPath:
            "c:/bloomdesktop/output/browser/templates/template books/activity/activity.html"
    };
    return (
        <PreviewFrame>
            <TemplateBookPagesWrapper
                firstGroup={false}
                templateBook={book}
                titleGroup={dummyTitleGroup}
                orientation="portrait"
                forChooseLayout={false}
                onTemplatePageSelect={() => {}}
                onTemplatePageDoubleClick={() => {}}
            />
        </PreviewFrame>
    );
};

TemplateBookPagesActivityPortrait.story = {
    name: "TemplateBookPages-activity-portrait"
};

export const TemplateBookPagesActivityLandscape = () => {
    const book: ITemplateBookInfo = {
        templateBookFolderUrl:
            "c:/bloomdesktop/output/browser/templates/template books/activity",
        templateBookPath:
            "c:/bloomdesktop/output/browser/templates/template books/activity/activity.html"
    };
    return (
        <PreviewFrame>
            <TemplateBookPagesWrapper
                firstGroup={false}
                templateBook={book}
                titleGroup={dummyTitleGroup}
                orientation="landscape"
                forChooseLayout={true}
                onTemplatePageSelect={() => {}}
                onTemplatePageDoubleClick={() => {}}
            />
        </PreviewFrame>
    );
};

TemplateBookPagesActivityLandscape.story = {
    name: "TemplateBookPages-activity-landscape"
};

export const TemplateBookPagesCustom = () => {
    const book: ITemplateBookInfo = {
        templateBookFolderUrl:
            "c:/users/gordon/documents/bloom/sokoro books test/gaadi template",
        templateBookPath:
            "c:/users/gordon/documents/bloom/sokoro books test/gaadi template/gaadi template.htm"
    };
    return (
        <PreviewFrame>
            <TemplateBookPagesWrapper
                firstGroup={false}
                templateBook={book}
                titleGroup={dummyTitleGroup}
                orientation="portrait"
                forChooseLayout={false}
                onTemplatePageSelect={() => {}}
                onTemplatePageDoubleClick={() => {}}
            />
        </PreviewFrame>
    );
};

TemplateBookPagesCustom.story = {
    name: "TemplateBookPages-custom"
};

export const _TemplateBookErrorReplacement = () => (
    <PreviewFrame>
        <TemplateBookErrorReplacement templateBookPath="Some bizarre location/My messed up Template/My messed up Template.htm" />
    </PreviewFrame>
);

_TemplateBookErrorReplacement.story = {
    name: "TemplateBookErrorReplacement"
};

export const _PageThumbnail = () => (
    <PreviewFrame>
        <PageThumbnail
            imageSource={getTemplatePageImageSource(
                "c:/bloomdesktop/output/browser/templates/template books/basic book",
                "Basic Text & Picture",
                "landscape"
            )}
            isLandscape={true}
        />
    </PreviewFrame>
);

_PageThumbnail.story = {
    name: "PageThumbnail"
};
