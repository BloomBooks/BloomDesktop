const kMsPerDay = 24 * 60 * 60 * 1000;

// Says roughly how long ago a date was, in whole days, weeks, months or years, localized for
// the given language: e.g. "today", "yesterday", "2 days ago", "3 weeks ago". Anything on
// the same calendar day as `now` (in the local time zone) is "today"; a date in the future
// (a clock difference between computers) also counts as today.
export function formatTimeAgo(
    isoDate: string,
    now: Date,
    locale: string,
): string {
    const then = new Date(isoDate);
    const startOfDay = (d: Date) =>
        new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
    const days = Math.max(
        0,
        Math.round((startOfDay(now) - startOfDay(then)) / kMsPerDay),
    );
    const format = new Intl.RelativeTimeFormat(locale, { numeric: "auto" });
    if (days < 7) return format.format(-days, "day");
    if (days < 30) return format.format(-Math.floor(days / 7), "week");
    if (days < 365) return format.format(-Math.floor(days / 30), "month");
    return format.format(-Math.floor(days / 365), "year");
}
