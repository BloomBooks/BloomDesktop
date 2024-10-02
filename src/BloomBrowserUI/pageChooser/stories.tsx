/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import { lightTheme } from "../bloomMaterialUITheme";
import * as React from "react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { storiesOf } from "@storybook/react";
import { addDecorator } from "@storybook/react";
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

// ENHANCE: Could we make this have the exact same dimensions the browser dialog would have?
addDecorator(storyFn => (
    <StyledEngineProvider injectFirst>
        <ThemeProvider theme={lightTheme}>
            <StorybookContext.Provider value={true}>
                {storyFn()}
            </StorybookContext.Provider>
        </ThemeProvider>
    </StyledEngineProvider>
));
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
                            pages: bloomPages
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

storiesOf("Page Chooser", module)
    .add("normal preview", () => {
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
                    enterpriseAvailable={true}
                    pageIsDigitalOnly={false}
                    pageId="1235"
                    templateBookPath="somePath"
                    isLandscape={false}
                ></SelectedTemplatePageControls>
            </PreviewFrame>
        );
    })
    .add("preview requiresEnterprise", () => {
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
                    pageIsEnterpriseOnly={true}
                    enterpriseAvailable={false}
                    pageIsDigitalOnly={true}
                    pageId="1235"
                    templateBookPath="somePath"
                    learnMoreLink="some-strange-link"
                    isLandscape={true}
                ></SelectedTemplatePageControls>
            </PreviewFrame>
        );
    })
    .add("preview changeLayoutWillLoseData", () => {
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
                    enterpriseAvailable={true}
                    pageIsDigitalOnly={false}
                    pageId="1235"
                    templateBookPath="somePath"
                    forChangeLayout={true}
                    willLoseData={true}
                    isLandscape={true}
                ></SelectedTemplatePageControls>
            </PreviewFrame>
        );
    })

    // Note: this group of tests has hard-coded paths to Bloom's compiler output (from c:!).
    // One of them has a hard-coded path from Gordon's machine! It will only be useful if
    // you substitute a path from your own machine.
    .add("TemplateBookPages", () => {
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
    })
    .add("TemplateBookPages-activity-portrait", () => {
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
    })
    .add("TemplateBookPages-activity-landscape", () => {
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
    })
    .add("TemplateBookPages-custom", () => {
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
    })
    .add("TemplateBookErrorReplacement", () => (
        <PreviewFrame>
            <TemplateBookErrorReplacement templateBookPath="Some bizarre location/My messed up Template/My messed up Template.htm" />
        </PreviewFrame>
    ))
    .add("PageThumbnail", () => (
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
    ));
