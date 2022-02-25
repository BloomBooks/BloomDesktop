import React = require("react");
import TriangleMenuIcon from "./TriangleMenu";
import { SvgIconProps } from "@material-ui/core/SvgIcon";

/**
 * A grey upside-down triangle menu icon.
 */
export const GreyTriangleMenuIcon = (props: SvgIconProps) => (
    <TriangleMenuIcon fillColor={"#F5F5F5"} {...props} />
);

export default GreyTriangleMenuIcon;
