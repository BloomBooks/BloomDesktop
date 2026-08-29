// The month and weekday names a new calendar grid starts out with, in the five languages the
// Wall Calendar template seeds. A grid built on a canvas gets them from here; the grid pages
// the template ships get them at build time from the same tables written in pug.
//
// KEEP IN STEP: the same two tables are written out in
// src/content/templates/template books/Wall Calendar/Wall Calendar.pug (kCalendarMonthNames
// and kCalendarWeekdayNames). Change one copy and you must change the other.

/** The five seed languages, in the order a translationGroup lists them. */
export const kCalendarSeedLanguages = ["en", "fr", "es", "id", "pt"];

/** The twelve month names of each seed language, January first. */
export const kMonthSeedNames: Record<string, string[]> = {
    en: [
        "January",
        "February",
        "March",
        "April",
        "May",
        "June",
        "July",
        "August",
        "September",
        "October",
        "November",
        "December",
    ],
    fr: [
        "janvier",
        "février",
        "mars",
        "avril",
        "mai",
        "juin",
        "juillet",
        "août",
        "septembre",
        "octobre",
        "novembre",
        "décembre",
    ],
    es: [
        "enero",
        "febrero",
        "marzo",
        "abril",
        "mayo",
        "junio",
        "julio",
        "agosto",
        "septiembre",
        "octubre",
        "noviembre",
        "diciembre",
    ],
    id: [
        "Januari",
        "Februari",
        "Maret",
        "April",
        "Mei",
        "Juni",
        "Juli",
        "Agustus",
        "September",
        "Oktober",
        "November",
        "Desember",
    ],
    pt: [
        "janeiro",
        "fevereiro",
        "março",
        "abril",
        "maio",
        "junho",
        "julho",
        "agosto",
        "setembro",
        "outubro",
        "novembro",
        "dezembro",
    ],
};

/**
 * The seven weekday names of each seed language, Sunday first. The layout code rotates the
 * header cells into the user's chosen first day of the week.
 */
export const kWeekdaySeedNames: Record<string, string[]> = {
    en: ["Sun", "Mon", "Tues", "Wed", "Thur", "Fri", "Sat"],
    fr: ["dim.", "lun.", "mar.", "mer.", "jeu.", "ven.", "sam."],
    es: ["dom.", "lun.", "mar.", "mié.", "jue.", "vie.", "sáb."],
    id: ["Min", "Sen", "Sel", "Rab", "Kam", "Jum", "Sab"],
    pt: ["dom.", "seg.", "ter.", "qua.", "qui.", "sex.", "sáb."],
};
