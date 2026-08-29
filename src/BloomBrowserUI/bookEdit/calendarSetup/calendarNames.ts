// The month and weekday names a calendar book shows, and the rules for keeping the names on a
// page and the names remembered for the collection in step with each other.
//
// The names live in two places. On a page they are ordinary bloom-translationGroups, so the
// user types them the way they type anything else, and the book carries them wherever it
// goes. In the collection they live in configuration.txt, read and written through the
// calendarSettings API, so the next calendar book in the same collection starts out with the
// names the user has already typed.
//
// Neither place simply wins. The rules below are what the two requirements come to:
//   - A book made in one collection and opened in another shows the names of the collection
//     it is now in, so the settings win the first time each page is opened in a session.
//   - Typing a weekday name on one month must not be undone by opening a later month, so
//     after that first open a page's own non-empty name is never overwritten.

/** What the calendarSettings API stores for a collection. */
export interface ICalendarSettings {
    /** Twelve names, January first. An empty string is a name the user has not typed. */
    monthNames: string[];
    /** Seven names, Sunday first. An empty string is a name the user has not typed. */
    dayNames: string[];
    /** 0 for Sunday through 6 for Saturday, or null if the user has not chosen one. */
    firstDayOfWeek: number | null;
}

/** What reconciling one name asks the caller to change. Both fields are absent for a name that is already in step. */
export interface ICalendarNameReconciliation {
    /** The value to write into the page, if the page has to change. */
    valueForPage?: string;
    /** The value to write into the settings, if the settings have to change. */
    valueForSettings?: string;
}

/**
 * Work out what to do about one name that a page and the collection's settings both have an
 * opinion about.
 *
 * On the first open of a page in a session the settings win: a name the collection knows
 * replaces a different name on the page, and a name only the page knows is taken into the
 * collection. On any later open in the same session the page is only ever filled in, never
 * overwritten, so a name the user types on one month survives their visiting another.
 */
export function reconcileCalendarName(
    pageValue: string,
    settingsValue: string,
    isFirstOpenOfPageThisSession: boolean,
): ICalendarNameReconciliation {
    const onPage = pageValue.trim();
    const inSettings = settingsValue.trim();
    if (isFirstOpenOfPageThisSession) {
        if (inSettings && inSettings !== onPage)
            return { valueForPage: inSettings };
        if (!inSettings && onPage) return { valueForSettings: onPage };
        return {};
    }
    if (!onPage && inSettings) return { valueForPage: inSettings };
    return {};
}

/**
 * The settings value a name on a saved page asks for, or undefined if the settings already
 * say what the page says. A name the user has emptied on the page does not empty the
 * collection's name: only what they typed is remembered.
 */
export function captureCalendarName(
    pageValue: string,
    settingsValue: string,
): string | undefined {
    const onPage = pageValue.trim();
    if (!onPage) return undefined;
    return onPage === settingsValue.trim() ? undefined : onPage;
}

/** Settings with every name empty and no first day of the week chosen. */
export function emptyCalendarSettings(): ICalendarSettings {
    return {
        monthNames: new Array(12).fill(""),
        dayNames: new Array(7).fill(""),
        firstDayOfWeek: null,
    };
}

/**
 * The given settings with every field present and the right length, so the rest of the code
 * can index into them without checking. A collection that has never held a calendar has no
 * calendar section in its configuration.txt at all.
 */
export function normalizeCalendarSettings(
    settings: Partial<ICalendarSettings> | null | undefined,
): ICalendarSettings {
    const normalized = emptyCalendarSettings();
    for (let i = 0; i < normalized.monthNames.length; i++) {
        normalized.monthNames[i] = settings?.monthNames?.[i] ?? "";
    }
    for (let i = 0; i < normalized.dayNames.length; i++) {
        normalized.dayNames[i] = settings?.dayNames?.[i] ?? "";
    }
    const firstDay = settings?.firstDayOfWeek;
    normalized.firstDayOfWeek =
        typeof firstDay === "number" && firstDay >= 0 && firstDay <= 6
            ? firstDay
            : null;
    return normalized;
}

/**
 * The year a calendar the user makes now is most likely to be for: this one until June, and
 * the next one from June on, which is when people start making next year's calendar. This is
 * the rule the Wall Calendar's old setup wizard used.
 */
export function defaultCalendarYear(today: Date = new Date()): number {
    return today.getFullYear() + (today.getMonth() >= 5 ? 1 : 0);
}
