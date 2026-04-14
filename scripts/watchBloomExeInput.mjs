export const isManualRestartCommand = (value) => {
    const normalized = value.trim().toLowerCase();
    return normalized === "" || normalized === "r" || normalized === "restart";
};
