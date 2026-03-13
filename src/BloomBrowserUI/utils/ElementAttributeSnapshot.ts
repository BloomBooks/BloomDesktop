export type ElementAttributeMap = {
    [attributeName: string]: string;
};

export class ElementAttributeSnapshot {
    private readonly attributes: ElementAttributeMap;

    private constructor(attributes: ElementAttributeMap) {
        this.attributes = attributes;
    }

    public static fromElement = (
        element: Element,
    ): ElementAttributeSnapshot => {
        const snapshot: ElementAttributeMap = {};
        for (let index = 0; index < element.attributes.length; index++) {
            const attribute = element.attributes.item(index);
            if (attribute) {
                snapshot[attribute.name] = attribute.value;
            }
        }

        return new ElementAttributeSnapshot(snapshot);
    };

    public restoreToElement = (element: Element): void => {
        const currentAttributeNames: string[] = [];
        for (let index = 0; index < element.attributes.length; index++) {
            const attribute = element.attributes.item(index);
            if (attribute) {
                currentAttributeNames.push(attribute.name);
            }
        }

        currentAttributeNames.forEach((attributeName) => {
            if (
                !Object.prototype.hasOwnProperty.call(
                    this.attributes,
                    attributeName,
                )
            ) {
                element.removeAttribute(attributeName);
            }
        });

        Object.keys(this.attributes).forEach((attributeName) => {
            element.setAttribute(attributeName, this.attributes[attributeName]);
        });
    };
}
