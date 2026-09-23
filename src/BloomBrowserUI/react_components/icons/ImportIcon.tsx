import SvgIcon, { SvgIconProps } from "@mui/material/SvgIcon";

// An "import into a document" icon (arrow pointing into a page), used for the
// "Import .bloomSource File(s)" collection menu item. Uses currentColor so it follows
// the menu text color.
export const ImportIcon = (props: SvgIconProps) => (
    <SvgIcon viewBox="0 0 18 16" {...props}>
        <path
            d="M12 8L8 4V7H0V9H8V12M18 14V2C18 1.46957 17.7893 0.960859 17.4142 0.585786C17.0391 0.210714 16.5304 0 16 0H4C3.46957 0 2.96086 0.210714 2.58579 0.585786C2.21071 0.960859 2 1.46957 2 2V5H4V2H16V14H4V11H2V14C2 14.5304 2.21071 15.0391 2.58579 15.4142C2.96086 15.7893 3.46957 16 4 16H16C16.5304 16 17.0391 15.7893 17.4142 15.4142C17.7893 15.0391 18 14.5304 18 14Z"
            fill="currentColor"
        />
    </SvgIcon>
);

export default ImportIcon;
