/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import { Div } from "../react_components/l10nComponents";
import { Checkbox } from "../react_components/checkbox";
import BloomButton from "../react_components/bloomButton";
import { RequiresSubscriptionNotice } from "../react_components/requiresSubscription";
import SmallNumberPicker from "../react_components/smallNumberPicker";
import { Link } from "../react_components/link";
import { useL10n } from "../react_components/l10nHooks";
import {
    kBloomBuff,
    kMutedTextGray,
    kWarningColor
} from "../bloomMaterialUITheme";
import { useGetFeatureStatus } from "../react_components/featureStatus";

interface ISelectedTemplatePageProps {
    caption: string | null;
    imageSource?: string;
    pageDescription: string | null;
    pageIsDigitalOnly: boolean;
    // In theory, in the future, a template page could require multiple subscription features.
    // If these were in different tiers, we would need to start handling an array.
    featureName?: string;
    pageIsMarkedBilingual?: boolean;
    templateBookPath: string;
    pageId: string;
    forChangeLayout?: boolean;
    willLoseData?: boolean;
    learnMoreLink?: string;
    requiredTool?: string;
    isLandscape: boolean;
    dataToolId: string;
    onSubmit?: (
        forChangeLayout: boolean,
        pageId: string,
        templateBookPath: string,
        convertAnywayChecked: boolean,
        willLoseData: boolean,
        convertWholeBookChecked: boolean,
        numberToAdd: number,
        dataToolId: string,
        requiredTool?: string
    ) => void;
}

// Displays a large preview of a template page in the Add Page or Change Layout dialog.
export const SelectedTemplatePageControls: React.FunctionComponent<ISelectedTemplatePageProps> = (
    props: ISelectedTemplatePageProps
) => {
    const previewPaneLeftPadding = 15;

    const [continueChecked, setContinueChecked] = useState(false);
    const [convertWholeBookChecked, setConvertWholeBookChecked] = useState(
        false
    );
    const minimumPagesToAdd = 1;
    const maximumPagesToAdd = 99;
    const [numberToAdd, setNumberToAdd] = useState<number>(minimumPagesToAdd);

    const captionKey = "TemplateBooks.PageLabel." + props.caption;
    const descriptionKey = "TemplateBooks.PageDescription." + props.caption;
    const buttonKey = props.forChangeLayout
        ? "EditTab.AddPageDialog.ChooseLayoutButton"
        : "EditTab.AddPageDialog.AddPageButton";
    const buttonEnglishText = props.forChangeLayout
        ? "Use This Layout"
        : "Add Page";

    const featureStatus = useGetFeatureStatus(props.featureName) ?? {
        enabled: true,
        visible: true
    };

    const isAddOrChoosePageButtonEnabled = (): boolean => {
        return !props.forChangeLayout || !props.willLoseData || continueChecked;
    };

    const numberOfPagesTooltip = useL10n(
        "Number of pages to add",
        "EditTab.AddPageDialog.NumberOfPagesTooltip",
        "For the number to the left of the ADD PAGE button"
    );

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                height: 100%;
                width: 420px; // if we put 100%, the preview changes width depending on description, etc.
                align-items: center;
            `}
        >
            {props.imageSource && (
                <div
                    css={css`
                        display: flex;
                        flex-direction: row;
                        align-items: flex-end;
                    `}
                >
                    <img
                        css={css`
                            border: 1px solid #b0dee4;
                            max-width: 98%;
                            max-height: 232px;
                            width: ${props.isLandscape ? "unset" : "150px"};
                            height: ${props.isLandscape ? "150px" : "unset"};
                        `}
                        src={props.imageSource}
                    />
                    {props.pageIsMarkedBilingual && (
                        <div
                            css={css`
                                font-size: 28pt;
                                color: ${kMutedTextGray};
                                margin-bottom: -8px;
                            `}
                        >
                            B
                        </div>
                    )}
                </div>
            )}
            <Div
                css={css`
                    font-weight: bold;
                    text-align: center;
                    font-size: large;
                    margin-top: 5px;
                `}
                l10nKey={captionKey}
            >
                {props.caption}
            </Div>
            <div
                css={css`
                    white-space: normal;
                    max-height: 150px;
                    line-height: 1.5em;
                    overflow-y: auto;
                    margin-right: ${previewPaneLeftPadding}px;
                    margin-left: ${previewPaneLeftPadding}px;
                    margin-top: 1em;
                    align-self: flex-start;
                `}
                id="previewDescriptionTextContainer"
            >
                <Div l10nKey={descriptionKey}>{props.pageDescription}</Div>
                {props.pageIsDigitalOnly && (
                    <Div l10nKey="EditTab.AddPageDialog.DigitalPage">
                        This kind of page will be included only in digital book
                        outputs, not in PDF.
                    </Div>
                )}
                {props.learnMoreLink && (
                    <div
                        css={css`
                            display: flex;
                            flex-direction: row;
                            justify-content: end;
                        `}
                    >
                        <Link
                            href={props.learnMoreLink}
                            l10nKey={"Common.LearnMore"}
                            l10nComment={
                                "A link or button that leads to something that tells the user more about what they just read."
                            }
                            openInExternalBrowser={true}
                        >
                            Learn More
                        </Link>
                    </div>
                )}
            </div>
            {props.forChangeLayout && featureStatus?.enabled && (
                <div>
                    <Checkbox
                        id="convertWholeBookCheckbox"
                        css={css`
                            margin-left: ${previewPaneLeftPadding}px;
                            margin-right: ${previewPaneLeftPadding}px;
                            margin-top: 10px;
                            .disabled {
                                color: ${kBloomBuff};
                            }
                        `}
                        l10nKey="EditTab.AddPageDialog.ChooseLayoutConvertBookCheckbox"
                        name="WholeBook"
                        checked={convertWholeBookChecked}
                        disabled={props.willLoseData && !continueChecked}
                        tristate={false}
                        onCheckChanged={() =>
                            setConvertWholeBookChecked(!convertWholeBookChecked)
                        }
                    >
                        Change all similar pages in this book to this layout.
                    </Checkbox>
                    {props.willLoseData && (
                        <div
                            css={css`
                                color: ${kWarningColor};
                                margin-left: 15px; // rather arbitrary. I'd like it to be left-aligned with the picture.
                                margin-right: 15px;
                                margin-top: 10px;
                                line-height: 1.5em;
                            `}
                        >
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
                                css={css`
                                    color: ${kWarningColor};
                                    margin-left: 15px;
                                    margin-right: 15px;
                                `}
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
            {featureStatus?.enabled && (
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        justify-content: flex-end;
                        flex: 1;
                    `}
                >
                    <div
                        css={css`
                            display: flex;
                            flex-direction: row;
                        `}
                    >
                        <BloomButton
                            css={css`
                                padding: 10px !important;
                                width: 200px;
                                font-weight: bold !important;
                                font-size: 10pt !important;

                                &:disabled {
                                    color: ${kBloomBuff};
                                    border: medium solid ${kBloomBuff};
                                }
                            `}
                            l10nKey={buttonKey}
                            hasText={true}
                            enabled={isAddOrChoosePageButtonEnabled()}
                            onClick={() => {
                                if (props.onSubmit)
                                    props.onSubmit(
                                        !!props.forChangeLayout,
                                        props.pageId,
                                        props.templateBookPath,
                                        continueChecked,
                                        !!props.willLoseData,
                                        convertWholeBookChecked,
                                        props.forChangeLayout
                                            ? -1
                                            : numberToAdd,
                                        props.dataToolId,
                                        props.requiredTool
                                    );
                            }}
                        >
                            {buttonEnglishText}
                        </BloomButton>
                        {!props.forChangeLayout && (
                            <div
                                css={css`
                                    max-width: 30px;
                                    position: absolute;
                                    margin-left: -45px;
                                    margin-top: 4px;
                                `}
                            >
                                <SmallNumberPicker
                                    minLimit={minimumPagesToAdd}
                                    maxLimit={maximumPagesToAdd}
                                    handleChange={setNumberToAdd}
                                    tooltip={numberOfPagesTooltip}
                                />
                            </div>
                        )}
                    </div>
                </div>
            )}
            {!featureStatus?.enabled && (
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        justify-content: flex-end;
                        flex: 1;
                    `}
                >
                    <RequiresSubscriptionNotice
                        featureName={props.featureName}
                    />
                </div>
            )}
        </div>
    );
};

export default SelectedTemplatePageControls;
