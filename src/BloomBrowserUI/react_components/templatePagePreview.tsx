import * as React from "react";
import { useState, useEffect } from "react";
import { Div } from "./l10n";
import { Checkbox } from "./checkbox";
import BloomButton from "./bloomButton";
import { BloomApi } from "../utils/bloomApi";
import { RequiresBloomEnterprise } from "./requiresBloomEnterprise";
import { addPageClickHandler } from "../pageChooser/page-chooser";

interface ITemplatePagePreviewProps {
    caption?: string;
    imageSource?: string;
    pageDescription?: string;
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
    const [continueChecked, setcontinueChecked] = useState(false);

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
        let thisPageNeedsEnterprise = false;
        if (pageNeedsEnterprise) {
            thisPageNeedsEnterprise = pageNeedsEnterprise;
        }
        return !enterpriseAvailable && thisPageNeedsEnterprise;
    };

    const isAddPageButtonEnabled = (): boolean => {
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
            <Div className="DescriptionText" l10nKey={descriptionKey}>
                {props.pageDescription}
            </Div>
            {props.forChangeLayout &&
                !enterpriseSubscriptionFault(props.pageIsEnterpriseOnly) && (
                    <div>
                        <Checkbox
                            id="convertWholeBookCheckbox"
                            wrapClassName="convertWholeBook"
                            l10nKey="EditTab.AddPageDialog.ChooseLayoutConvertBookCheckbox"
                            name="WholeBook"
                            disabled={props.willLoseData && !continueChecked}
                            tristate={false}
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
                                        setcontinueChecked(!continueChecked)
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
                        id="addPageButton"
                        hasText={true}
                        enabled={isAddPageButtonEnabled()}
                        onClick={() =>
                            addPageClickHandler(
                                props.forChangeLayout
                                    ? props.forChangeLayout
                                    : false,
                                props.pageId,
                                props.templateBookPath
                            )
                        }
                    >
                        {buttonEnglishText}
                    </BloomButton>
                </div>
            )}
            {props.pageIsEnterpriseOnly && (
                <div className="pushToBottom">
                    <RequiresBloomEnterprise />
                </div>
            )}
        </div>
    );
};

export default TemplatePagePreview;
