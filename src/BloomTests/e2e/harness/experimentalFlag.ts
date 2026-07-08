// Automates the "experimental feature flag must be set in user.config BEFORE launching
// instances" step called out in Design/CloudTeamCollections/orchestration/09-e2e.prompt.md
// (the same hack recorded in IMPLEMENTATION.md's Wave-3 merge log: "missing experimental-
// feature checkbox — flag set via user.config for now — proper Advanced-settings checkbox
// still owed").
//
// Cloud Team Collections is gated behind ExperimentalFeatures.kCloudTeamCollections
// ("cloud-team-collections", src/BloomExe/ExperimentalFeatures.cs), stored as a comma-
// separated list in the *shared, per-machine* SIL.Settings CrossPlatformSettingsProvider
// user.config at `%LOCALAPPDATA%\SIL\<Product>\<Version>\user.config` (Product/Version come
// from BloomExe.csproj's <Product>/<Version>, currently "Bloom"/"6.5.0.0" — NOT the unrelated
// plain .NET "Bloom.exe_Url_<hash>" config folder). This file is shared across ALL Bloom
// instances under this Windows user account (it is not per-collection or per-identity), so
// it only needs to be ensured once per session, not per-launched-instance.
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { repoRoot } from "./paths";

const FEATURE_TOKEN = "cloud-team-collections";

const readCsprojField = async (
    field: "Product" | "Version",
): Promise<string> => {
    const csprojPath = path.join(
        repoRoot,
        "src",
        "BloomExe",
        "BloomExe.csproj",
    );
    const content = await fs.readFile(csprojPath, "utf8");
    const match = content.match(new RegExp(`<${field}>([^<]+)</${field}>`));
    if (!match) {
        throw new Error(`Could not find <${field}> in ${csprojPath}.`);
    }
    return match[1];
};

const userConfigPath = async (): Promise<string> => {
    const localAppData = process.env.LOCALAPPDATA;
    if (!localAppData) {
        throw new Error(
            "LOCALAPPDATA is not set — cannot locate Bloom's user.config.",
        );
    }
    const [product, version] = await Promise.all([
        readCsprojField("Product"),
        readCsprojField("Version"),
    ]);
    return path.join(localAppData, "SIL", product, version, "user.config");
};

/** Idempotently ensures the "cloud-team-collections" token is present in
 * EnabledExperimentalFeatures. Reads the existing user.config (created by any prior Bloom run;
 * if it doesn't exist yet, a real Bloom launch creates it, which the harness's first launch
 * will have already done for other settings) and does a minimal string edit — this file is the
 * developer's real settings (MRU list, window bounds, etc.), so we never overwrite it wholesale. */
export const ensureExperimentalFeatureEnabled = async (): Promise<void> => {
    const configPath = await userConfigPath();

    let content: string;
    try {
        content = await fs.readFile(configPath, "utf8");
    } catch {
        // No user.config yet on this machine/version. Bloom creates one with defaults on its
        // very first run; a bare-bones one that Bloom will happily merge with its schema also
        // works, and avoids a chicken-and-egg dependency on having already launched once.
        content = MINIMAL_USER_CONFIG;
        await fs.mkdir(path.dirname(configPath), { recursive: true });
    }

    const settingMatch = content.match(
        /<setting name="EnabledExperimentalFeatures"[^>]*>\s*<value>([^<]*)<\/value>/,
    );

    if (settingMatch) {
        const tokens = settingMatch[1]
            .split(",")
            .map((token) => token.trim())
            .filter(Boolean);
        if (tokens.includes(FEATURE_TOKEN)) {
            return; // already enabled — nothing to do
        }
        tokens.push(FEATURE_TOKEN);
        const updated = content.replace(
            /(<setting name="EnabledExperimentalFeatures"[^>]*>\s*<value>)([^<]*)(<\/value>)/,
            `$1${tokens.join(",")}$3`,
        );
        await fs.writeFile(configPath, updated, "utf8");
        return;
    }

    // Setting element doesn't exist yet in this user.config — insert one into
    // Bloom.Properties.Settings (or, for a from-scratch minimal file, it's already present).
    if (!content.includes("<Bloom.Properties.Settings>")) {
        throw new Error(
            `${configPath} has no <Bloom.Properties.Settings> section to add EnabledExperimentalFeatures to. ` +
                `Launch Bloom manually once on this machine first, then re-run the harness.`,
        );
    }
    const insertion = `<Bloom.Properties.Settings>\n      <setting name="EnabledExperimentalFeatures" serializeAs="String">\n        <value>${FEATURE_TOKEN}</value>\n      </setting>`;
    const updated = content.replace("<Bloom.Properties.Settings>", insertion);
    await fs.writeFile(configPath, updated, "utf8");
};

const MINIMAL_USER_CONFIG = `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <userSettings>
    <Bloom.Properties.Settings>
      <setting name="EnabledExperimentalFeatures" serializeAs="String">
        <value>${FEATURE_TOKEN}</value>
      </setting>
    </Bloom.Properties.Settings>
  </userSettings>
</configuration>
`;
