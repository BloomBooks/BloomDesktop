/*
bloom-player-core is responsible for all the behavior of working through a book, but without any UI controls
(other than page turning).
*/
import * as React from "react";
import axios from "axios";
import { AxiosPromise } from "axios";
import Slider from "react-slick";
import "slick-carousel/slick/slick.css";
import "slick-carousel/slick/slick-theme.css";
// This loads some JS right here that is a polyfill for the (otherwise discontinued) scoped-styles html feature
import "style-scoped/scoped"; // maybe use .min.js after debugging?
import "./bloom-player.less";
import Narration from "./narration";

// BloomPlayer takes the URL of a folder containing a Bloom book. The file name
// is expected to match the folder name. (Enhance: might be better to just take
// the full path to the book. Longer, but removes that assumption.)
// It displays pages from the book and allows them to be turned by dragging.
// On a wide screen, an option may be used to show the next and previous pages
// beside the current one.

interface IProps {
    url: string; // of the bloom book (folder)
    showContextPages?: boolean;
    // ``paused`` allows the parent to control pausing of audio. We expect we may supply
    // a click/touch event callback if needed to support pause-on-touch.
    paused?: boolean;

    // reportBookProperties is called when book loaded enough to determine these properties.
    // Not sure this is the best design, but it saves the client doing a lot
    // of duplicate work retrieving and processing the HTML to figure out these things.
    reportBookProperties?: (
        properties: { landscape: boolean; canRotate: boolean }
    ) => void;
    // called for initial page and subsequent page changes, passed the slider page
    // (the parent of the .bloom-page, including also the special element that carries
    // all the page styles)
    pageSelected?: (sliderPage: HTMLElement) => void;
}
interface IState {
    pages: Array<string>; // of the book. First and last are empty in context mode.
    styleRules: string; // concatenated stylesheets the book references or embeds.
    // indicates current page, though typically not corresponding to the page
    // numbers actually on the page. This is an index into pages, and in context
    // mode it's the index of the left context page, not the main page.
    currentSliderIndex: number;
}
export default class BloomPlayerCore extends React.Component<IProps, IState> {
    public readonly state: IState = {
        pages: ["loading..."],
        styleRules: "",
        currentSliderIndex: 0
    };

    private sourceUrl: string;

    private narration: Narration;

    // We expect it to show some kind of loading indicator on initial render, then
    // we do this work. For now, won't get a loading indicator if you change the url prop.
    public componentDidUpdate() {
        if (!this.narration) {
            this.narration = new Narration();
        }
        let newSourceUrl = this.props.url;
        // Folder urls often (but not always) end in /. If so, remove it, so we don't get
        // an empty filename or double-slashes in derived URLs.
        // enhance typescript: when we somehow configure a version that knows about
        // endsWith, we can remove this cast.
        if ((newSourceUrl as any).endsWith("/")) {
            newSourceUrl = newSourceUrl.substring(0, newSourceUrl.length - 1);
        }
        if (newSourceUrl != this.sourceUrl && newSourceUrl) {
            this.sourceUrl = newSourceUrl;
            this.narration.urlPrefix = this.sourceUrl;
            const index = this.sourceUrl.lastIndexOf("/");
            const filename = this.sourceUrl.substring(index + 1);
            // TODO: right now, this takes a url to the folder. Change to a url to the file.
            // Note: In the future, we are thinking of limiting to
            // a few domains (localhost, dev.blorg, blorg).
            const urlOfBookHtmlFile = this.sourceUrl + "/" + filename + ".htm"; // enhance: search directory if name doesn't match?
            axios.get(urlOfBookHtmlFile).then(result => {
                // we *think* this gets garbage collected
                const doc = document.createElement("html"); // TODO: would this work if it was "holderForBook"? JH was tripped up by html, thinking we were going to replace ourselves
                doc.innerHTML = result.data;

                const body = doc.getElementsByTagName("body")[0];
                const canRotate = body.hasAttribute("data-bfcanrotate"); // expect value allOrientations;bloomReader, should we check?

                this.makeNonEditable(body);

                // assemble the page content list
                const pages = doc.getElementsByClassName("bloom-page");
                const sliderContent: string[] = [];
                if (this.props.showContextPages) {
                    sliderContent.push(""); // blank page to fill the space left of first.
                }
                for (let i = 0; i < pages.length; i++) {
                    const page = pages[i];
                    const landscape = this.forceDevicePageSize(page);
                    // Now we have all the information we need to call reportBookProps if it is set.
                    if (i === 0 && this.props.reportBookProperties) {
                        this.props.reportBookProperties({
                            landscape: landscape,
                            canRotate: canRotate
                        });
                    }

                    this.fixRelativeUrls(page);

                    sliderContent.push(page.outerHTML);
                }
                if (this.props.showContextPages) {
                    sliderContent.push(""); // blank page to fill the space right of last.
                }

                this.assembleStyleSheets(doc);
                this.setState({ pages: sliderContent });
                // A pause hopefully allows the document to become visible before we
                // start playing any audio or movement on the first page.
                // Also gives time for the first page
                // element to actually get created in the document.
                // Note: typically in Chrome we won't actually start playing, because
                // of a rule that the user must interact with the document first.
                window.setTimeout(() => this.showingPage(0), 500);
            });
        }
        if (this.props.paused) {
            this.narration.pause();
        } else {
            this.narration.play();
        }
    }

    private makeNonEditable(body: HTMLBodyElement): void {
        // This is a preview, it's distracting to have it be editable.
        // (Should not occur in .bloomd, but might in books direct from BL.)
        const editable = document.evaluate(
            ".//*[@contenteditable]",
            body,
            null,
            XPathResult.UNORDERED_NODE_SNAPSHOT_TYPE,
            null
        );
        for (let iedit = 0; iedit < editable.snapshotLength; iedit++) {
            (editable.snapshotItem(iedit) as HTMLElement).removeAttribute(
                "contenteditable"
            );
        }
    }

    // Force size class to be one of the device classes
    // return true if we determine that the book is landscape
    private forceDevicePageSize(page: Element): boolean {
        let landscape = false;
        const classAttr = page.getAttribute("class") || "";
        const matches = classAttr.match(/\b\S*?(Portrait|Landscape)\b/);
        if (matches && matches.length) {
            const sizeClass = matches[0];
            landscape = (sizeClass as any).endsWith("Landscape");
            const desiredClass = landscape
                ? "Device16x9Landscape"
                : "Device16x9Portrait";
            if (sizeClass != desiredClass) {
                page.classList.remove(sizeClass);
                page.classList.add(desiredClass);
            }
        }
        return landscape;
    }

    // urls of images and videos and audio need to be made
    // relative to the original book folder, not the page we are embedding them into.
    private fixRelativeUrls(page: Element) {
        const srcElts = document.evaluate(
            ".//*[@src]",
            page,
            null,
            XPathResult.UNORDERED_NODE_SNAPSHOT_TYPE,
            null
        );

        for (let j = 0; j < srcElts.snapshotLength; j++) {
            const item = srcElts.snapshotItem(j) as HTMLElement;
            if (!item) {
                continue;
            }
            const srcName = item.getAttribute("src");
            const srcPath = this.fullUrl(srcName);
            item.setAttribute("src", srcPath);
        }
    }

    // Assemble all the style rules from all the stylesheets the book contains or references.
    // When we finish (not before this method returns), the result will be set as
    // our state.styles with setState().
    private assembleStyleSheets(doc: HTMLHtmlElement) {
        const linkElts = document.evaluate(
            ".//link[@href and @type='text/css']",
            doc,
            null,
            XPathResult.UNORDERED_NODE_SNAPSHOT_TYPE,
            null
        );
        const promises: AxiosPromise<any>[] = [];
        for (let i = 0; i < linkElts.snapshotLength; i++) {
            const link = linkElts.snapshotItem(i) as HTMLElement;
            const href = link.getAttribute("href");
            const fullHref = this.fullUrl(href);
            promises.push(axios.get(fullHref));
        }
        axios
            .all(
                promises.map(p =>
                    p.catch(
                        // if one stylesheet doesn't exist or whatever, keep going
                        () => undefined
                    )
                )
            )
            .then(results => {
                let combinedStyle = "";

                // start with embedded styles (typically before links in a bloom doc...)
                const styleElts = document.evaluate(
                    ".//style[@type='text/css']",
                    doc,
                    null,
                    XPathResult.UNORDERED_NODE_SNAPSHOT_TYPE,
                    null
                );
                for (let k = 0; k < styleElts.snapshotLength; k++) {
                    const styleElt = styleElts.snapshotItem(k) as HTMLElement;
                    combinedStyle += styleElt.innerText;
                }

                // then add the stylesheet contents we just retrieved
                results.forEach(result => {
                    if (result && result.data) {
                        combinedStyle += result.data;
                    }
                });
                this.setState({ styleRules: combinedStyle });
            });
    }

    private fullUrl(url: string | null): string {
        // Enhance: possibly we should only do this if we somehow determine it is a relative URL?
        // But the things we apply it to always are, in bloom books.
        return this.sourceUrl + "/" + url;
    }

    private slider: Slider | null;

    public render() {
        // multiple classes help make rules more specific than those in the book's stylesheet
        // (which benefit from an extra attribute item like __scoped_N)
        // It would be nice to use an ID but we don't want to assume there is
        // only one of these components on a page.
        return (
            <div className="bloomPlayer bloomPlayer1">
                <Slider
                    className="pageSlider"
                    ref={slider => (this.slider = slider)}
                    slidesToShow={this.props.showContextPages ? 3 : 1}
                    infinite={false}
                    dots={this.props.showContextPages}
                    beforeChange={(current, next) => this.setIndex(next)}
                    afterChange={current => this.showingPage(current)}
                >
                    {this.state.pages.map((slide, index) => {
                        return (
                            <div
                                key={slide}
                                className={
                                    "page-preview-slide" +
                                    this.getSlideClass(index)
                                }
                            >
                                <style scoped={true}>
                                    {this.state.styleRules}
                                </style>
                                <div
                                    className="actual-page-preview"
                                    dangerouslySetInnerHTML={{ __html: slide }}
                                    onClick={() =>
                                        this.slider!.slickGoTo(index - 1)
                                    }
                                />
                            </div>
                        );
                    })}
                </Slider>
            </div>
        );
    }

    // Get a class to apply to a particular slide. This is used to apply the
    // contextPage class to the slides before and after the current one.
    private getSlideClass(itemIndex: number): string {
        if (!this.props.showContextPages) {
            return "";
        }
        if (
            itemIndex === this.state.currentSliderIndex ||
            itemIndex === this.state.currentSliderIndex + 2
        ) {
            return "contextPage";
        }
        return "";
    }

    // Called from beforeChange, sets up context classes
    private setIndex(index: number) {
        this.setState({ currentSliderIndex: index });
    }

    // Called from afterChange, starts narration, etc.
    private showingPage(index: number): void {
        const sliderPage = document.querySelectorAll(
            ".slick-slide[data-index='" +
                (index + (this.props.showContextPages ? 1 : 0)) +
                "'"
        )[0] as HTMLElement;
        if (!sliderPage) {
            return; // unexpected
        }
        const bloomPage = sliderPage.getElementsByClassName(
            "bloom-page"
        )[0] as HTMLElement;
        if (!bloomPage) {
            return; // blank initial or final page?
        }
        this.narration.computeDuration(bloomPage);
        this.narration.playAllSentences(bloomPage);
        if (this.props.pageSelected) {
            this.props.pageSelected(sliderPage);
        }
    }
}
