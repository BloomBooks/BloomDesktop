import { css } from "@emotion/react";
import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
import { ButtonBase, Popover } from "@mui/material";
import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import {
    getDefaultAppBuilderIconChoice,
    useAppBuilderIconChoices,
} from "./appBuilderAppDef";
import { getBloomLocalFileUrl } from "./appBuilderShared";

export const AppBuilderIconChooserForConfigr: React.FunctionComponent<{
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
}> = (props) => {
    const currentIconLabel = useL10n(
        "Current icon",
        "PublishTab.Apps.SettingsDialog.Icon.Current",
    );
    const chooseIconLabel = useL10n(
        "Choose App Icon",
        "PublishTab.Apps.SettingsDialog.IconChooseTitle",
    );
    const availableChoices = useAppBuilderIconChoices();
    const defaultChoice = getDefaultAppBuilderIconChoice(availableChoices);
    const [anchorElement, setAnchorElement] = React.useState<HTMLElement>();
    const hasCurrentValue =
        !!props.value &&
        !availableChoices.some((choice) => choice.iconPath === props.value);
    const choices = hasCurrentValue
        ? [
              {
                  id: "current",
                  label: currentIconLabel,
                  iconPath: props.value,
              },
              ...availableChoices,
          ]
        : availableChoices;
    const selectedIconPath = props.value || defaultChoice?.iconPath || "";
    const selectedChoice = choices.find(
        (availableChoice) => availableChoice.iconPath === selectedIconPath,
    );
    const isPickerOpen = !!anchorElement;

    function renderIcon(
        iconPath: string,
        label: string,
        isSelected?: boolean,
        showBorder: boolean = true,
    ) {
        return (
            <div
                css={css`
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    width: 44px;
                    height: 44px;
                    overflow: hidden;
                    background: #fff;
                    border: 2px solid
                        ${showBorder
                            ? isSelected
                                ? "#1d5fbf"
                                : "#d7d7d7"
                            : "transparent"};
                    border-radius: 4px;
                    flex: 0 0 auto;
                    box-shadow: ${showBorder && isSelected
                        ? "0 0 0 1px rgba(29, 95, 191, 0.2)"
                        : "none"};
                `}
                title={label}
                aria-label={label}
            >
                {iconPath && (
                    <img
                        key={iconPath}
                        src={getBloomLocalFileUrl(iconPath)}
                        alt=""
                        css={css`
                            max-width: 100%;
                            max-height: 100%;
                            object-fit: contain;
                        `}
                        onLoad={(event) => {
                            event.currentTarget.style.visibility = "visible";
                        }}
                        onError={(event) => {
                            event.currentTarget.style.visibility = "hidden";
                        }}
                    />
                )}
            </div>
        );
    }

    function closePicker(): void {
        setAnchorElement(undefined);
    }

    return (
        <>
            <ButtonBase
                type="button"
                disabled={props.disabled}
                aria-label={chooseIconLabel}
                aria-haspopup="dialog"
                aria-expanded={isPickerOpen}
                onClick={(event) => {
                    setAnchorElement(event.currentTarget);
                }}
                css={css`
                    display: inline-flex;
                    align-items: center;
                    justify-content: center;
                    gap: 2px;
                    width: 72px;
                    min-height: 56px;
                    padding: 4px 6px;
                    background: white;
                    border: 1px solid rgba(0, 0, 0, 0.23);
                    border-radius: 4px;

                    &:hover {
                        border-color: rgba(0, 0, 0, 0.87);
                    }

                    &:focus-visible {
                        outline: 2px solid #1d5fbf;
                        outline-offset: 1px;
                    }

                    &:disabled {
                        opacity: 0.55;
                    }
                `}
            >
                {renderIcon(
                    selectedIconPath,
                    selectedChoice?.label ??
                        defaultChoice?.label ??
                        currentIconLabel,
                    false,
                    false,
                )}
                <ArrowDropDownIcon
                    css={css`
                        color: rgba(0, 0, 0, 0.54);
                    `}
                />
            </ButtonBase>
            <Popover
                open={isPickerOpen}
                anchorEl={anchorElement}
                onClose={closePicker}
                anchorOrigin={{
                    vertical: "bottom",
                    horizontal: "left",
                }}
                transformOrigin={{
                    vertical: "top",
                    horizontal: "left",
                }}
                PaperProps={{
                    css: css`
                        margin-top: 4px;
                    `,
                }}
            >
                <div
                    role="listbox"
                    aria-label={chooseIconLabel}
                    css={css`
                        display: grid;
                        grid-template-columns: repeat(5, minmax(0, 1fr));
                        gap: 8px;
                        padding: 10px;
                        max-width: 320px;
                        background: white;
                    `}
                >
                    {choices.map((choice) => {
                        const isSelected = choice.iconPath === selectedIconPath;

                        return (
                            <ButtonBase
                                key={choice.id}
                                type="button"
                                role="option"
                                aria-selected={isSelected}
                                title={choice.label}
                                onClick={() => {
                                    props.onChange(choice.iconPath);
                                    closePicker();
                                }}
                                css={css`
                                    display: inline-flex;
                                    align-items: center;
                                    justify-content: center;
                                    width: 52px;
                                    height: 52px;
                                    padding: 4px;
                                    border-radius: 6px;
                                    background: ${isSelected
                                        ? "rgba(29, 95, 191, 0.08)"
                                        : "transparent"};

                                    &:hover {
                                        background: ${isSelected
                                            ? "rgba(29, 95, 191, 0.12)"
                                            : "rgba(0, 0, 0, 0.04)"};
                                    }

                                    &:focus-visible {
                                        outline: 2px solid #1d5fbf;
                                        outline-offset: 1px;
                                    }
                                `}
                            >
                                {renderIcon(
                                    choice.iconPath,
                                    choice.label,
                                    isSelected,
                                )}
                            </ButtonBase>
                        );
                    })}
                </div>
            </Popover>
        </>
    );
};
