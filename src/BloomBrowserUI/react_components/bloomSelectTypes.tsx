// Only the first two properties of IOption are used by BloomSelect.
export interface IOption {
    value: string;
    label: string;
    l10nKey?: string;
    comment?: string;
}

export interface IBloomSelectProps {
    currentOption: IOption; // Only currentOption.value is used in BloomSelect.
    options: IOption[];
    nullOption: string; // The IOption .value associated with not having chosen one of the real options
    className: string;
}
