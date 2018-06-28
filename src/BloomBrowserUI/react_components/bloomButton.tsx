import { BloomApi } from "../utils/bloomApi";
import * as React from "react";
import * as ReactDOM from "react-dom";
import {
    ILocalizationProps,
    ILocalizationState,
    LocalizableElement
} from "./l10n";

export interface IButtonProps extends ILocalizationProps {
    enabled: boolean;
    clickEndpoint: string;
    hasText: boolean; // allows us to define buttons with only images and no text.
    // If neither enabled or disabled image file is provided, no image will show.
    // If only one is provided, no image will show in the other state (e.g. if disabled and no disabledImageFile).
    enabledImageFile?: string;
    disabledImageFile?: string;
    l10nTipEnglishEnabled?: string; // existence of these two strings (or one of them) enables tooltips on the button.
    l10nTipEnglishDisabled?: string;
}

// A button that takes a Bloom API endpoint to post() when clicked
// and a bool to determine if this button should currently be enabled.
// The button displays localizable text and optionally enabled and disabled versions of an image file.
// Also, this button optionally implements the LocalizableElement tooltip function.
export default class BloomButton extends LocalizableElement<
    IButtonProps,
    ILocalizationState
    > {
    constructor(props: IButtonProps) {
        super(props);
    }

    private getButtonImage(): string {
        let image = null;
        if (this.props.enabled && this.props.enabledImageFile) {
            image = <img src={this.props.enabledImageFile} />;
        } else if (!this.props.enabled && this.props.disabledImageFile) {
            image = <img src={this.props.disabledImageFile} />;
        }
        return image;
    }

    public render() {
        const image = this.getButtonImage();
        let tip: string = null;
        if (
            this.props.l10nTipEnglishEnabled ||
            this.props.l10nTipEnglishDisabled
        ) {
            tip = this.getLocalizedTooltip(this.props.enabled);
        }
        return (
            <button
                className={
                    this.props.className + (this.props.hidden ? " hidden" : "")
                }
                title={tip}
                onClick={() =>
                    BloomApi.post("api/" + this.props.clickEndpoint)
                }
                disabled={!this.props.enabled}
            >
                {image}
                {this.props.hasText && this.getLocalizedContent()}
            </button>
        );
    }
}
