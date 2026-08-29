// The one question a new Wall Calendar book asks: which year is it for, and which day does
// its weeks start on. Everything else about the calendar is filled in as the user moves
// through the book. See calendarTooling.ts, which shows this.

import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import { MenuItem, TextField } from "@mui/material";
import { renderRootSync } from "../../utils/reactRender";
import { postBoolean } from "../../utils/bloomApi";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    Mode,
    useSetupBloomDialog,
} from "../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { defaultCalendarYear } from "./calendarNames";

/** What the user chose, or undefined if they cancelled. */
export interface ICalendarSetupChoice {
    year: number;
    /** 0 for Sunday through 6 for Saturday. */
    firstDayOfWeek: number;
}

/** The translator note the seven weekday choices share. */
const kDayOfWeekComment =
    "One of the seven choices in the Calendar Setup dialog's 'First day of the week' list.";

/**
 * The seven weekday names, Sunday first because Sunday is 0 in JavaScript's Date.
 *
 * The calls are written out rather than looped because a hook cannot be called in a loop, and
 * they are here rather than in a MenuItem wrapper component because MUI's Select reads the
 * `value` prop off its own direct children: a wrapper hides that and the list stops working.
 */
function useDayOfWeekLabels(): string[] {
    return [
        useL10n("Sunday", "CalendarSetup.Sunday", kDayOfWeekComment),
        useL10n("Monday", "CalendarSetup.Monday", kDayOfWeekComment),
        useL10n("Tuesday", "CalendarSetup.Tuesday", kDayOfWeekComment),
        useL10n("Wednesday", "CalendarSetup.Wednesday", kDayOfWeekComment),
        useL10n("Thursday", "CalendarSetup.Thursday", kDayOfWeekComment),
        useL10n("Friday", "CalendarSetup.Friday", kDayOfWeekComment),
        useL10n("Saturday", "CalendarSetup.Saturday", kDayOfWeekComment),
    ];
}

export const CalendarSetupDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    initialFirstDayOfWeek: number;
    onFinished: (choice: ICalendarSetupChoice | undefined) => void;
}> = (props) => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);
    show = showDialog;
    cancel = () => finish(undefined);

    const [year, setYear] = useState<string>(String(defaultCalendarYear()));
    const [firstDayOfWeek, setFirstDayOfWeek] = useState<number>(
        props.initialFirstDayOfWeek,
    );

    const dayOfWeekLabels = useDayOfWeekLabels();
    const dialogTitle = useL10n("Calendar Setup", "CalendarSetup.Title");
    const yearLabel = useL10n("Year", "CalendarSetup.Year");
    const firstDayLabel = useL10n(
        "First day of the week",
        "CalendarSetup.FirstDayOfWeek",
    );

    // Disable the rest of the edit tab while the dialog is up. The page list lives outside
    // the modal's own document, so nothing else would grey it out. EditingView counts these
    // rather than treating them as a flag, so every true must be matched by exactly one false;
    // hence acting on the change to open, and not on the first render, when it is already false.
    const wasOpen = React.useRef(false);
    React.useEffect(() => {
        const isOpen = !!propsForBloomDialog.open;
        if (isOpen === wasOpen.current) return;
        wasOpen.current = isOpen;
        postBoolean("editView/setModalState", isOpen);
    }, [propsForBloomDialog.open]);

    // Four digits is what a year is; anything else is a typing accident, not a calendar.
    const yearIsUsable = /^\d{4}$/.test(year.trim());

    const finish = (choice: ICalendarSetupChoice | undefined) => {
        closeDialog();
        props.onFinished(choice);
    };

    return (
        <BloomDialog
            {...propsForBloomDialog}
            onCancel={() => finish(undefined)}
            css={css`
                .MuiDialog-paperWidthSm {
                    max-width: 420px;
                }
            `}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    display: flex;
                    flex-direction: column;
                    gap: 20px;
                    min-width: 300px;
                    // The outlined fields' floating labels sit above the field's border, so
                    // without this the first label is clipped by the scroll container.
                    padding-top: 8px;
                `}
            >
                <TextField
                    id="calendar-setup-year"
                    label={yearLabel}
                    variant="outlined"
                    size="small"
                    value={year}
                    error={!yearIsUsable}
                    onChange={(event) => setYear(event.target.value)}
                    inputProps={{ inputMode: "numeric", maxLength: 4 }}
                    css={css`
                        max-width: 140px;
                    `}
                />
                <TextField
                    id="calendar-setup-first-day-of-week"
                    name="calendar-setup-first-day-of-week"
                    label={firstDayLabel}
                    variant="outlined"
                    size="small"
                    select
                    value={firstDayOfWeek}
                    onChange={(event) =>
                        setFirstDayOfWeek(parseInt(event.target.value, 10))
                    }
                >
                    {dayOfWeekLabels.map((label, day) => (
                        <MenuItem key={day} value={day}>
                            {label}
                        </MenuItem>
                    ))}
                </TextField>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    enabled={yearIsUsable}
                    onClick={() =>
                        finish({
                            year: parseInt(year.trim(), 10),
                            firstDayOfWeek,
                        })
                    }
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

let show: () => void = () => {
    window.alert("CalendarSetupDialog is not set up yet.");
};
let cancel: () => void = () => {};

// Whether a shown dialog has not been answered yet. A dialog left open when its book is
// closed has to be dismissed properly, not just removed from the DOM: closing it is what
// posts the matching editView/setModalState false, and skipping that leaves the page list
// and the workspace tabs disabled for good.
let dialogIsPending = false;

/**
 * Dismiss a shown, unanswered Calendar Setup dialog as if the user had cancelled it. Safe to
 * call when no dialog is up. The tooling calls this when a different book is opened while the
 * dialog is still waiting for an answer.
 */
export function closeCalendarSetupDialogIfOpen(): void {
    if (dialogIsPending) cancel();
}

/**
 * Ask the user which year the calendar is for and which day its weeks start on. Resolves with
 * their answer, or with undefined if they cancelled, in which case the book is left
 * unconfigured and we ask again the next time they open it.
 */
export function showCalendarSetupDialog(
    savedFirstDayOfWeek: number | null,
): Promise<ICalendarSetupChoice | undefined> {
    closeCalendarSetupDialogIfOpen();
    return new Promise((resolve) => {
        renderRootSync(
            <CalendarSetupDialog
                initialFirstDayOfWeek={savedFirstDayOfWeek ?? 0}
                onFinished={(choice) => {
                    dialogIsPending = false;
                    resolve(choice);
                }}
                dialogEnvironment={{
                    dialogFrameProvidedExternally: false,
                    initiallyOpen: false,
                    mode: Mode.Edit,
                }}
            />,
            getModalContainer(),
        );
        dialogIsPending = true;
        show();
    });
}

// A container of our own, for the same reason TopicChooserDialog has one: sharing the edit
// view's single modal container produces strange interactions between dialogs.
function getModalContainer(): HTMLElement {
    document.getElementById("CalendarSetupDialogContainer")?.remove();
    const container = document.createElement("div");
    container.id = "CalendarSetupDialogContainer";
    document.body.appendChild(container);
    return container;
}
