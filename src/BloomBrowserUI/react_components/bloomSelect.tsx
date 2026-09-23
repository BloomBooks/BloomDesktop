import * as React from "react";
import { useState } from "react";
import Select, { SelectProps } from "@mui/material/Select";
import { callOnBlur } from "../utils/menuCloseOnBlur";

const BloomSelect = <TValue,>(
    props: SelectProps<TValue>,
): React.ReactElement => {
    const [isOpen, setIsOpen] = useState(false);

    const handleOpen: NonNullable<SelectProps<TValue>["onOpen"]> = (event) => {
        setIsOpen(true);
        props.onOpen?.(event);
        callOnBlur(() => setIsOpen(false));
    };

    const handleClose: NonNullable<SelectProps<TValue>["onClose"]> = (
        event,
    ) => {
        setIsOpen(false);
        props.onClose?.(event);
    };

    return (
        <Select
            {...props}
            open={isOpen}
            onOpen={handleOpen}
            onClose={handleClose}
        />
    );
};

export default BloomSelect;
