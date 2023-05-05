import * as _ from "underscore";
import * as $ from "jquery";
import "../../../../modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js"; //for dialog()
import "errorHandler";
import "jquery.i18n.custom.ts"; //localize()
import "long-press/jquery.mousewheel.js";
import "long-press/jquery.longpress.js";
import theOneLocalizationManager from "../../../../lib/localizationManager/localizationManager";

import "../jquery.div-columns.ts";
import "../libSynphony/jquery.text-markup.ts";
import { ReaderStage, ReaderLevel, ReaderSettings } from "../ReaderSettings";
import {
    theOneLanguageDataInstance,
    theOneLibSynphony
} from "../libSynphony/synphony_lib";

import { DataWord, TextFragment } from "../libSynphony/bloomSynphonyExtensions";
import "./readerSetup.io";
import "./readerSetup.ui";

// //script(src='/bloom/lib/errorHandler.js')
// script(src='/bloom/bookEdit/toolbox/decodableReader/jquery.div-columns.js')
// script(src='/bloom/bookEdit/toolbox/decodableReader/libSynphony/underscore_min_152.js')
// script(src='/bloom/bookEdit/toolbox/decodableReader/libSynphony/jquery.text-markup.js')
// script(src='/bloom/bookEdit/toolbox/decodableReader/libSynphony/synphony_lib.js')
// script(src='/bloom/bookEdit/toolbox/decodableReader/libSynphony/bloomSynphonyExtensions.js')
// script(src='/bloom/lib/localizationManager/localizationManager.js')
// script(src='/bloom/lib/jquery.i18n.custom.js')
// script(src='/bloom/lib/long-press/jquery.mousewheel.js')
// script(src='/bloom/lib/long-press/jquery.longpress.js')
// script(src='/bloom/bookEdit//toolbox/decodableReader/readerSettings.js')s.js')

// script(src='/bloom/bookEdit/toolbox/decodableReader/readerSetup/readerSetup.io.js')
// script(src='/bloom/bookEdit/toolbox/decodableReader/readerSetup/readerSetup.ui.js')
// script(type='text/javascript') $(function() {$("#dlstabs").tabs();});

//was $(function() {$("#dlstabs").tabs();});
$(document).ready(() => {
    $("#dlstabs").tabs();
});
