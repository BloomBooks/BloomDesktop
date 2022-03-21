/** @jsx jsx **/
import { jsx, css, SerializedStyles } from "@emotion/core";
import * as React from "react";
import { TabsProps, Tabs } from "react-tabs";
interface IProps extends TabsProps {
    color: string; // text, borders
    // text and bottom border for selected tab
    selectedColor: string;
    labelBackgroundColor: string;
    // more CSS to apply to the Tabs component as a whole
    // Note SerializedStyles is what you get as the output of an Emotion css`` function.
    // So, you can say rootCss={css`whatever`}
    rootCss?: SerializedStyles;
    // more CSS to apply to the individual labels (tabs)
    labelCss?: SerializedStyles;
    // more CSS to apply to the particular label (tab) that is selected
    selectedLabelCss?: SerializedStyles;
    // more CSS to apply to the whole group of labels
    labelGroupCss?: SerializedStyles;
    // more CSS to apply to each of the content panes
    contentPaneCss?: SerializedStyles;
}

// A wrapper around the react-tabs Tab element that applies some standard CSS we want in Bloom,
// and inserts some of the configurable elements in the proper places. This is a work-in-progress;
// as we identify more common behavior we want or more things we often want to configure we can
// improve it.
export const BloomTabs: React.FunctionComponent<IProps> = props => {
    const {
        color,
        selectedColor,
        labelBackgroundColor,
        rootCss,
        labelCss,
        selectedLabelCss,
        labelGroupCss,
        contentPaneCss,
        ...tabsProps
    } = props;
    return (
        <Tabs
            {...tabsProps}
            css={css`
                ${rootCss}
                .react-tabs__tab.react-tabs__tab {
                    background-color: ${labelBackgroundColor};
                    color: ${color};
                    text-transform: uppercase;
                    ${labelCss}
                }
                .react-tabs__tab--selected {
                    color: ${selectedColor} !important;
                    border-color: transparent;
                    border-bottom: 2px solid ${selectedColor};
                    ${selectedLabelCss}
                }
                .react-tabs__tab-list {
                    border: none;
                    ${labelGroupCss}
                }
                .react-tabs__tab-panel {
                    ${contentPaneCss}
                }
            `}
        >
            {props.children}
        </Tabs>
    );
};
