/**
 * Created by Thomson on 1/19/2016.
 */
// Informs typescript that jquery.easytabs extends JQuery with function easytabs
interface JQuery {
    easytabs(options: any): JQuery;
    // enhance: this belongs in its own d.ts file, but I can't find the component that makes this extension to JQuery.
    sort(compare: (a: HTMLElement, b: HTMLElement) => number): JQuery;
}
