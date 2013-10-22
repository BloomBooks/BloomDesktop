// Type definitions for toolbarjs


interface ToolBarOptions {
	content: string;
	position?: string;
	hideOnClick?: bool;
	zIndex?: number;
}

interface JQuery {
	toolbar(): JQuery;
	toolbar(options: ToolBarOptions): JQuery;
}