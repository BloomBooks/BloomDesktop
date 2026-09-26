// The cursor shown while the Edit tab changes pages: a plain clock face, rather than the spinning
// circle Windows draws for the standard "wait" cursor. Falls back to that standard cursor.
const kClockSvg =
    '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">' +
    '<circle cx="12" cy="12" r="10" fill="white" stroke="#333" stroke-width="2"/>' +
    '<path d="M12 12V6.5M12 12l3.5 2" stroke="#333" stroke-width="2" stroke-linecap="round"/>' +
    "</svg>";

export const kWaitCursor = `url("data:image/svg+xml,${encodeURIComponent(
    kClockSvg,
)}") 12 12, wait`;
