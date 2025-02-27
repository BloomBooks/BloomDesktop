import eslint from "@eslint/js";
import reactPlugin from "eslint-plugin-react";
import globals from "globals";
import hooksPlugin from "eslint-plugin-react-hooks";
// Copilot says this should be @typescript-eslint/eslint-plugin, but that results in
// one of the configs not being found
import tseslint from "typescript-eslint";
import eslintPluginPrettierRecommended from "eslint-plugin-prettier/recommended";

export default [
    eslint.configs.recommended, // Recommended config applied to all files
    reactPlugin.configs.flat.recommended, // Recommended for react
    reactPlugin.configs.flat["jsx-runtime"], // Recommended for react 17+ (new?)
    tseslint.configs.recommended, // recommended for typescript
    tseslint.configs.recommendedTypeChecked, // this might be new?
    eslintPluginPrettierRecommended,
    hooksPlugin.configs.recommended,

    {
        files: [
            "**/*.js",
            //"**/*.jsx", don't thnk we have any of these?
            "**/*.ts",
            "**/*.tsx"
        ],
        // these might not need the closing /**
        ignores: [
            "**/node_modules/**",
            "**/dist/**",
            "**/Readium/**",
            "**/modified_libraries/**"
        ],
        // review: do we need these in addition to the recommended configs above?
        plugins: {
            react: reactPlugin,
            "react-hooks": hooksPlugin
        },
        languageOptions: {
            ...reactPlugin.configs.flat.recommended.languageOptions,
            // not working: hopefully covered by tseslint.configs.recommended
            //parser: "@typescript-eslint/parser",
            parserOptions: {
                ecmaVersion: "latest",
                sourceType: "module",
                ecmaFeatures: {
                    jsx: true
                }
            },
            globals: {
                // I think this replaces the env secion of the old .eslintrc.js
                ...globals.browser,
                ...globals.es2021,
                ...globals.jasmine,
                ...globals.jquery
            }
        },
        settings: {
            react: {
                version: "detect" // React version. "detect" automatically picks the version you have installed.
            }
        },
        rules: {
            // should there be something here for reactPlugin?
            ...hooksPlugin.configs.recommended.rules,
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
    }
];
