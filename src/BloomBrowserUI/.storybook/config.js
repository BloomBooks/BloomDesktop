import { configure } from "@storybook/react";

// load all files named "stories.tsx"
const req = require.context("../", true, /stories\.tsx$/);

function loadStories() {
    req.keys().forEach(filename => req(filename));
}

configure(loadStories, module);
