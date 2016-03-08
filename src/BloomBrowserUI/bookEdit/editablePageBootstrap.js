//("commonBundle.js";

import "jquery";
import "../modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js";
import "../lib/jquery.qtip.js";
import "../lib/jquery.qtipSecondary.js";

// first tried this as import 'jquery.hotkeys' in bloomEditing, but that didn't work
import "jquery.hotkeys.js";


import "editablePageBundle.js";

// John Thomson: Added this last because currently its document ready function has to execute AFTER the bootstrap call in bloomEditing.ts,
// which is compiled into editablePageIFrame.js. The bootstrap function sets CKEDITOR.disableAutoInline = true,
// which suppresses a document ready function in CKEditor iself from calling inline() on all content editable
// elements, which we don't want (a) because some content editable elements shouldn't have CKEditor functions, and
// (b) because it causes crashes when we intentionally do our own inline() calls on the elements where we DO
// want CKEditor.
// ReviewSlog: It would be much more robust not to depend on the order in which document ready functions
// execute, especially if the only control over that is the order of loading files. But I don't know
// where we can put the CKEDITOR.disableAutoInline = true so that it will reliably execute AFTER CKEDITOR is
// defined and BEFORE its document ready function.
import "lib/ckeditor/ckeditor.js";