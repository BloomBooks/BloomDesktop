const { default: TruncateMarkup } = require("react-truncate-markup");

module.exports = {
    env: {
        browser: true,
        es2021: true,
        jasmine: true,
        jquery: true
    },
    extends: [
        "eslint:recommended",
        "plugin:react/recommended",
        "plugin:react-hooks/recommended",
        "plugin:@typescript-eslint/recommended",
        "plugin:prettier/recommended"
    ],
    parser: "@typescript-eslint/parser",
    parserOptions: {
        ecmaFeatures: {
            jsx: true
        },
        ecmaVersion: "latest",
        sourceType: "module"
    },
    plugins: ["react", "@typescript-eslint"],
    settings: {
        react: {
            version: "detect" // React version. "detect" automatically picks the version you have installed.
        }
    },
    rules: {
        // Rules to apply on top of the baseline ones (from "extends")
        // FYI, to see all the rule settings, run "eslint --print-config *.ts"
        "prettier/prettier": "off",
        "react/no-unknown-property": ["error", { ignore: ["css"] }], // allow emotion css: https://emotion.sh/docs/eslint-plugin-react
        "no-var": "warn",
        "prefer-const": "warn",
        "no-useless-escape": "off",
        "no-irregular-whitespace": [
            "error",
            { skipStrings: true, skipTemplates: true }
        ],
        "no-warning-comments": [
            1,
            { terms: ["nocommit"], location: "anywhere" }
        ],
        // Downgraded from error to warnings
        "@typescript-eslint/no-empty-function": "warn",
        "@typescript-eslint/no-empty-interface": "warn",
        "@typescript-eslint/no-explicit-any": "warn",
        "@typescript-eslint/no-unused-vars": [
            "warn",
            { argsIgnorePattern: "^_", varsIgnorePattern: "^_" }
        ],
        "@typescript-eslint/no-var-requires": "warn",
        "no-case-declarations": "warn",
        "prefer-rest-params": "warn",
        "prefer-spread": "warn",
        eqeqeq: ["warn", "always"],
        // Disabled
        "@typescript-eslint/ban-types": "off", // Record<string, never> is not intuitive for us compared to {}
        "@typescript-eslint/no-inferrable-types": "off", // not worth worrying about (not even convinced it's a problem at all)
        "@typescript-eslint/triple-slash-reference": "off", // a lot of our legacy code still uses this
        "react/no-unescaped-entities": "off", // Complains about some special chars that sort of work, but due to the burden that enocded chars present to localizers, we'd prefer not to encode them if not necessary.
        "react/prop-types": "off" // Seems to require validation on the props parameter itself, but Typescript can already figure out the types through annotations in different places, seems unnecessary
    }
};
