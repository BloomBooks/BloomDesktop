// For more info, see https://github.com/storybookjs/eslint-plugin-storybook#configuration-flat-config-format
import storybook from "eslint-plugin-storybook";

import eslint from "@eslint/js";
import reactPlugin from "eslint-plugin-react";
import globals from "globals";
import hooksPlugin from "eslint-plugin-react-hooks";
// Copilot says this should be @typescript-eslint/eslint-plugin, but that results in
// one of the configs not being found. It also contradicts https://typescript-eslint.io/getting-started/
import tseslint from "typescript-eslint";
import eslintPluginPrettierRecommended from "eslint-plugin-prettier/recommended";

export default [
    // Recommended config applied to all files
    eslint.configs.recommended, // Recommended for react
    reactPlugin.configs.flat.recommended, // Recommended for react 17+ (new?)
    reactPlugin.configs.flat["jsx-runtime"], // the doc for typescript-eslint does not have the ..., but without it we get a weird error
    // saying TypeError: Unexpected array
    // recommended for type-aware typescript linting
    ...tseslint.configs.recommendedTypeChecked,
    eslintPluginPrettierRecommended, // this object exists, but it seems to be in eslintrc mode, and eslint 9 chokes.
    // Instead, I list it as a plugin and import the rules directly into the rules section.
    //hooksPlugin.configs.recommended,
    {
        // alone in an object, these apply to every config object
        // these might not need the closing /**
        ignores: [
            // other people's code, in some cases with small patches, but not stuff
            // we want to modify drastically to make lint happy
            "**/node_modules/**",
            "**/Readium/**",
            "**/modified_libraries/**",
            // don't need to check stuff we output
            "**/dist/**",
            // These are type definitions we mostly got from elsewhere, not complete enough
            // to make typescript happy.
            "**/typings/**",
            // I don't think we have any js we want to lint, at least not using a typescript
            // parser. Most of our js files are other people's code, already excluded above.
            // Also, as of Mar 2025, we are running prettier v1 which cannot handle some of these files
            // and causes eslint to fail completely if we have eslintPluginPrettierRecommended enabled as above.
            // If we had much JS, it would probably benefit from linting even more than TS.
            // But as long as we don't, I think this can stand.
            "**/*.js",
        ],
    },
    {
        files: [
            //"**/*.js", // parsing with ts rules, js files generate massive errors
            //"**/*.jsx", don't think we have any of these?
            "**/*.ts",
            "**/*.tsx",
        ],

        // review: do we need these in addition to the recommended configs above?
        // seems to work without them
        plugins: {
            react: reactPlugin,
            "react-hooks": hooksPlugin,
        },
        languageOptions: {
            // not working: hopefully covered by tseslint.configs.recommended
            //parser: "@typescript-eslint/parser",
            parserOptions: {
                ecmaVersion: "latest",
                sourceType: "module",
                ecmaFeatures: {
                    jsx: true,
                },
                projectService: true,
                tsconfigRootDir: import.meta.dirname,
            },
            globals: {
                // I think this replaces the env section of the old .eslintrc.js
                ...globals.browser,
                ...globals.es2021,
            },
        },
        settings: {
            react: {
                version: "detect", // React version. "detect" automatically picks the version you have installed.
            },
        },
        rules: {
            ...hooksPlugin.configs.recommended.rules,
            // Rules to apply on top of the baseline ones (from the various config blocks above)
            // FYI, to see all the rule settings, run "eslint --print-config *.ts"
            "prettier/prettier": "off",
            "react/no-unknown-property": ["error", { ignore: ["css"] }], // allow emotion css: https://emotion.sh/docs/eslint-plugin-react
            "no-var": "warn",
            "prefer-const": "warn",
            "no-useless-escape": "off",
            "no-irregular-whitespace": [
                "error",
                { skipStrings: true, skipTemplates: true },
            ],
            "no-warning-comments": [
                1,
                { terms: ["nocommit"], location: "anywhere" },
            ],
            // Downgraded from error to warnings
            "@typescript-eslint/no-empty-function": [
                "warn",
                { allow: ["arrowFunctions"] },
            ],
            "@typescript-eslint/no-empty-interface": "warn",
            "@typescript-eslint/no-explicit-any": "warn",
            "@typescript-eslint/no-unused-vars": [
                "warn",
                { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
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
            "react/prop-types": "off", // Seems to require validation on the props parameter itself, but Typescript can already figure out the types through annotations in different places, seems unnecessary
            "no-irregular-whitespace": "off",
            // If you want to temporarily reduce warnings, these four account for 100% as of Aug 2025
            // "@typescript-eslint/no-explicit-any": "off",
            // "@typescript-eslint/no-unused-vars": "off",
            // eqeqeq: "off",
            // "react-hooks/exhaustive-deps": "off"
        },
    }, // Add a specific override for Storybook files
    {
        files: ["**/*.stories.tsx", "**/stories.tsx"],
        rules: {
            // Disable React hooks rules for Storybook files
            "react-hooks/rules-of-hooks": "off",
            "react-hooks/exhaustive-deps": "off",
        },
    },
    ...storybook.configs["flat/recommended"],
];
