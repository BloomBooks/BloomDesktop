/**
 * Created by Thomson on 1/19/2016.
 */
// Informs typescript that jquery.i18n.custom extends JQuery with function localize
interface JQuery {
    localize(callbackDone?: Function): void;
}
