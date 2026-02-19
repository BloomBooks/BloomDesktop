import SvgIcon, { SvgIconProps } from "@mui/material/SvgIcon";

interface TriangleMenuIconProps {
    fillColor: string;
}

/**
 * An icon that represents the upside-down triangle (chevron) menu
 * @param props Requires fillColor. Must not pass viewBox, this component sets its own viewBox.
 */
export const TriangleMenuIcon = (
    props: TriangleMenuIconProps & Omit<SvgIconProps, "viewBox">,
) => {
    // NOTE: Not sure why, but SvgIcon's htmlColor prop didn't seem to help much.
    // So just explicitly ask for the fillColor
    const { fillColor, ...passThroughProps } = props;
    const viewBox = "0, 0, 13, 18"; // Makes the SVG a 13x18 icon.
    return (
        <SvgIcon {...passThroughProps} viewBox={viewBox}>
            {/* Start at (0, 8)
            Horizontal line of width 12. i.e. move to (12, 8)
            Line to (6, 17.5)
            Line back to starting point (0, 8)
            */}
            <path d="M0 8 h 12 L 6 17.5 L 0 8" fill={fillColor} />
        </SvgIcon>
    );
};

export default TriangleMenuIcon;
