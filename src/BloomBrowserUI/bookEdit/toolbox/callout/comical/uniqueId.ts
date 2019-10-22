// modify an SVG element to use unique internal IDs.
// based on ideas from https://github.com/elderapo/react-svg-unique-id
// This seems to do enough for what Comical currently needs;
// it's not clear that it's enough for any conceivable svg that might be
// used to define a bubble.

export function uniqueIds(e: Element) {
    const idElements = e.ownerDocument!.evaluate(
        ".//*[@id]",
        e,
        null,
        XPathResult.UNORDERED_NODE_SNAPSHOT_TYPE,
        null
    );
    const map = {};
    const guid: string = "i" + createUuid();
    for (let i = 0; i < idElements.snapshotLength; i++) {
        const idElement = idElements.snapshotItem(i) as HTMLElement;
        const id = idElement.getAttribute("id");
        if (id) {
            const newId = guid + id;
            map[id] = newId;
            idElement.setAttribute("id", newId);
        }
    }
    fixElement(e, map);
}

// adapted from http://stackoverflow.com/questions/105034/create-guid-uuid-in-javascript
function createUuid(): string {
    // http://www.ietf.org/rfc/rfc4122.txt
    var s: string[] = [];
    var hexDigits = "0123456789abcdef";
    for (var i = 0; i < 36; i++) {
        s[i] = hexDigits.substr(Math.floor(Math.random() * 0x10), 1);
    }
    s[14] = "4"; // bits 12-15 of the time_hi_and_version field to 0010
    s[19] = hexDigits.substr((s[19] as any & 0x3) | 0x8, 1); // bits 6-7 of the clock_seq_hi_and_reserved to 01
    s[8] = s[13] = s[18] = s[23] = "-";

    var uuid = s.join("");
    return uuid;
}

function fixElement(e: Element, map: object) {
    for (let i = 0; i < e.children.length; i++) {
        fixElement(e.children[i], map);
    }
    for (let j = 0; j < e.attributes.length; j++) {
        var attrib = e.attributes[j];
        if (
            attrib.value &&
            attrib.value.startsWith("url(#") &&
            attrib.value.endsWith(")")
        ) {
            const id = attrib.value.substring(
                "url(#".length,
                attrib.value.length - 1
            );
            const newId = map[id];
            if (newId) {
                e.setAttribute(attrib.name, "url(#" + newId + ")");
            }
        }
    }
}
