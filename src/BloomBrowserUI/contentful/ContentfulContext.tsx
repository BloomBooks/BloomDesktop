import * as Contentful from "contentful";
const kContentfulSpace = "72i7e2mqidxz";

// there's a problem with the TS types in the Contentful library, hence this "any"
const contentfulClientPublished = Contentful.createClient({
    accessToken: "XPudkny5JX74w0dxrwqS_WY3GUBA5xO_AzFR7fwO2aE",
    space: kContentfulSpace
});

// using the preview key, we can access draft materials for easy previewing while working on content
const contentfulClientPreview = Contentful.createClient({
    accessToken: "2WiMEBo1hKnLwRjXTzGSX5Zid-UfUcIfJd_JaR43Irs",
    space: kContentfulSpace,
    host: "preview.contentful.com"
});

// Should return contentfulClientPreview if we want to see previews.
// Enhance: figure out how to turn that on.
export function getContentfulClient(): Contentful.ContentfulClientApi {
    return contentfulClientPublished;
}
