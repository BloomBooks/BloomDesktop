import React = require("react");

// Just indicates to components that we are in storybook mode,
// and so they should expect axios calls to the Bloom Desktop server fail.
// Alternative would be to mock all axios calls?
export const StorybookContext = React.createContext(false);
