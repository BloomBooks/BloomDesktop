import * as React from "react";

export class SimpleMenuItem {
    text: string;
    l10nKey: string;
    temporarilyDisableI18nWarning?: boolean;
    action: () => void; // in addition to closing the menu
    disabled?: boolean;
    icon?: React.ReactNode;
}
