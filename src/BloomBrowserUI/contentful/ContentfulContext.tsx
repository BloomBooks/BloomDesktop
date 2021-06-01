import * as Contentful from "contentful";
const kContentfulSpace = "72i7e2mqidxz";

// there's a problem with the TS types in the Contentful library, hence this "any"
const contentfulClientPublished = Contentful.createClient({
    accessToken: "XPudkny5JX74w0dxrwqS_WY3GUBA5xO_AzFR7fwO2aE",
    space: kContentfulSpace
});

// BloomLibrary also has a previewKey, which allows seeing unpublished content.
// If we need it, we can arrange in appropriate cases to return that here.
export function getContentfulClient(): Contentful.ContentfulClientApi {
    return contentfulClientPublished;
}
