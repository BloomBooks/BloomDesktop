import * as React from "react";
import { useState, useEffect } from "react";
import { Div } from "../react_components/l10nComponents";
import { Checkbox } from "../react_components/checkbox";
import BloomButton from "../react_components/bloomButton";
import { BloomApi } from "../utils/bloomApi";
import { RequiresBloomEnterprise } from "../react_components/requiresBloomEnterprise";
import { addPageClickHandler as addOrChoosePageClickHandler } from "./page-chooser";

interface ITemplatePagePreviewProps {
    caption?: string;
    imageSource?: string;
    pageDescription?: string;
    pageIsDigitalOnly: boolean;
    pageIsEnterpriseOnly?: boolean;
    templateBookPath: string;
    pageId: string;
    forChangeLayout?: boolean;
    willLoseData?: boolean;
}

// Displays a large preview of a template page in the Add Page or Change Layout dialog.
export const TemplatePagePreview: React.FunctionComponent<
    ITemplatePagePreviewProps
> = (props: ITemplatePagePreviewProps) => {
    const [enterpriseAvailable, setEnterpriseAvailable] = useState(true);
    const [continueChecked, setContinueChecked] = useState(false);
    const [convertWholeBookChecked, setConvertWholeBookChecked] = useState(
        false
    );

    useEffect(() => {
        BloomApi.get("common/enterpriseFeaturesEnabled", response => {
            setEnterpriseAvailable(response.data);
        });
    }, []);

    const captionKey = "TemplateBooks.PageLabel." + props.caption;
    const descriptionKey =
        "TemplateBooks.PageDescription." + props.pageDescription;
    const buttonKey = props.forChangeLayout
        ? "EditTab.AddPageDialog.ChooseLayoutButton"
        : "EditTab.AddPageDialog.AddPageButton";
    const buttonEnglishText = props.forChangeLayout
        ? "Use This Layout"
        : "Add Page";

    // If this function returns <true>, we need to let the user know that they need a
    // Bloom Enterprise subscription to use this page.
    const enterpriseSubscriptionFault = (
        pageNeedsEnterprise: boolean | undefined
    ): boolean => {
        return !enterpriseAvailable && !!pageNeedsEnterprise;
    };

    const isAddOrChoosePageButtonEnabled = (): boolean => {
        return !props.forChangeLayout || !props.willLoseData || continueChecked;
    };

    return (
        <div className="previewWrapper">
            {props.imageSource && (
                <img className="previewImage" src={props.imageSource} />
            )}
            <Div className="previewCaption" l10nKey={captionKey}>
                {props.caption}
            </Div>
            <div id="previewDescriptionTextContainer">
                <Div l10nKey={descriptionKey}>{props.pageDescription}</Div>
                {props.pageIsDigitalOnly && (
                    <Div l10nKey="EditTab.AddPageDialog.DigitalPage">
                        This kind of page will be included only in digital book
                        outputs, not in PDF.
                    </Div>
                )}
            </div>
            {props.forChangeLayout &&
                !enterpriseSubscriptionFault(props.pageIsEnterpriseOnly) && (
                    <div>
                        <Checkbox
                            id="convertWholeBookCheckbox"
                            wrapClassName="convertWholeBook"
                            l10nKey="EditTab.AddPageDialog.ChooseLayoutConvertBookCheckbox"
                            name="WholeBook"
                            checked={convertWholeBookChecked}
                            disabled={props.willLoseData && !continueChecked}
                            tristate={false}
                            onCheckChanged={() =>
                                setConvertWholeBookChecked(
                                    !convertWholeBookChecked
                                )
                            }
                        >
                            Change all similar pages in this book to this
                            layout.
                        </Checkbox>
                        {props.willLoseData && (
                            <div className="convertLosesMaterial">
                                <Div l10nKey="EditTab.AddPageDialog.ChooseLayoutWillLoseData">
                                    Converting to this layout will cause some
                                    content to be lost.
                                </Div>
                                <Checkbox
                                    id="convertAnywayCheckbox"
                                    l10nKey="EditTab.AddPageDialog.ChooseLayoutContinueCheckbox"
                                    name="Continue"
                                    checked={continueChecked}
                                    tristate={false}
                                    wrapClassName="convertAnyway"
                                    onCheckChanged={() =>
                                        setContinueChecked(!continueChecked)
                                    }
                                >
                                    Continue anyway
                                </Checkbox>
                            </div>
                        )}
                    </div>
                )}
            {!enterpriseSubscriptionFault(props.pageIsEnterpriseOnly) && (
                <div className="pushToBottom">
                    <BloomButton
                        l10nKey={buttonKey}
                        id="addOrChoosePageButton"
                        hasText={true}
                        enabled={isAddOrChoosePageButtonEnabled()}
                        onClick={() =>
                            addOrChoosePageClickHandler(
                                !!props.forChangeLayout,
                                props.pageId,
                                props.templateBookPath,
                                continueChecked,
                                !!props.willLoseData,
                                convertWholeBookChecked
                            )
                        }
                    >
                        {buttonEnglishText}
                    </BloomButton>
                </div>
            )}
            {enterpriseSubscriptionFault(props.pageIsEnterpriseOnly) && (
                <div className="pushToBottom">
                    <RequiresBloomEnterprise />
                </div>
            )}
        </div>
    );
};

export default TemplatePagePreview;
