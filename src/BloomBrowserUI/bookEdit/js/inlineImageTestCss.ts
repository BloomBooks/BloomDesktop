// The stylesheets that lay out inline images, as CSS text, for browser tests that put them on
// their test page. They are imported here rather than in a test file: a test file that calls
// vi.mock has its imports turned into dynamic ones, and in browser mode a dynamic import of a
// .less file from src/content fails to load.
import inlineImagesCss from "../../../content/bookLayout/inlineImages.less?inline";
import inlineImageEditingCss from "../css/inlineImageEditing.less?inline";

export const kInlineImageTestCss = `${inlineImagesCss}\n${inlineImageEditingCss}`;
