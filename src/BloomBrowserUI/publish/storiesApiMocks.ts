import { mockReplies } from "../utils/bloomApi";

// enough to get us a minimal BookMetadataDialog

mockReplies["book/metadata"] = {
    data: {
        metadata: {
            author: {
                type: "editableText",
                value: "Shmo",
                translatedLabel: "Author"
            },
            hazards: {
                type: "hazards",
                value: "",
                translatedLabel: "Hazards",
                helpurl:
                    "http://www.idpf.org/epub/a11y/techniques/techniques.html#meta-004"
            },
            typicalAgeRange: {
                type: "editableText",
                value: "",
                translatedLabel: "Typical age range"
            }
        },
        translatedStringPairs: {
            alternativeText: "Has Image Descriptions",
            flashingHazard: "Flashing Hazard",
            motionSimulationHazard: "Motion Simulation Hazard",
            signLanguage: "Sign Language"
        }
    }
};
