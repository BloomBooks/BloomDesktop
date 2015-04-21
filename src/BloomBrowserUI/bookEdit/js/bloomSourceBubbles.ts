/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="bloomQtipUtils.ts" />

interface qtipInterface extends JQuery {
    qtip(options: any): JQuery;
    qtipSecondary(options: any): JQuery;
}