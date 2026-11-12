/** The table that holds `table`, and holds no other table itself. */
export function outermostTable(table: HTMLElement): HTMLElement {
    let outermost = table;
    let parent = outermost.parentElement?.closest<HTMLElement>(".bloom-table");
    while (parent) {
        outermost = parent;
        parent = outermost.parentElement?.closest<HTMLElement>(".bloom-table");
    }
    return outermost;
}
