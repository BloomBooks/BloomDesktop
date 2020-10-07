/**
 * Created by Thomson on 1/19/2016.
 */
// Informs typescript that jquery.qtip extends JQuery with function qtip
interface JQuery {
    qtip(options: any): JQuery;
}
