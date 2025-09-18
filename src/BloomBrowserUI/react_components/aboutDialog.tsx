import { css } from "@emotion/react";
import { useEventLaunchedBloomDialog } from "../react_components/BloomDialog/BloomDialogPlumbing";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogProps,
} from "./BloomDialog/BloomDialog";
import { DialogCloseButton } from "./BloomDialog/commonDialogComponents";
import { useL10n } from "./l10nHooks";
import { getBloomApiPrefix, postString, useApiString } from "../utils/bloomApi";
import { Div } from "./l10nComponents";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { ShowEditViewDialog } from "../bookEdit/editViewFrame";

export const AboutDialogLauncher: React.FunctionComponent = () => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useEventLaunchedBloomDialog("AboutDialog");

    show = showDialog;

    return propsForBloomDialog.open ? (
        <AboutDialog
            closeDialog={closeDialog}
            showDialog={showDialog}
            propsForBloomDialog={propsForBloomDialog}
        />
    ) : null;
};

export const AboutDialog: React.FunctionComponent<{
    closeDialog: () => void;
    showDialog: () => void;
    propsForBloomDialog: IBloomDialogProps;
}> = (props) => {
    const logoURI = getBloomApiPrefix(false) + "images/SIL_Logo_80pxTall.png";

    const buildNumber = useApiString("app/versionNumber", "");
    const builtOnDate = useApiString("app/versionBuildDate", "");

    const closeDialog = () => {
        // notify the server that we're closing the dialog.
        postString("app/closeDialog", "AboutDialog");
        props.closeDialog();
    };

    return (
        <BloomDialog {...props.propsForBloomDialog}>
            <DialogTitle
                title={useL10n(
                    "About {0}",
                    "AboutDialog.AboutBloom",
                    "Dialog Title",
                    "Bloom",
                )}
            />
            <DialogMiddle
                css={css`
                    height: 500px;
                    display: grid;
                    grid-template-columns: 150px auto; // Logo only needs specific amount of space
                `}
            >
                <div
                    css={css`
                        display: grid;
                        align-content: space-between;
                        max-width: 140px;
                    `}
                >
                    <div
                        css={css`
                            max-width: inherit;
                            word-wrap: break-word;
                        `}
                    >
                        <br></br>
                        {
                            <img
                                src={logoURI}
                                css={css`
                                    max-width: inherit;
                                `}
                            />
                        }
                    </div>
                    <div
                        css={css`
                            max-width: inherit;
                            word-wrap: break-word;
                        `}
                    >
                        <div>{buildNumber}</div>
                        <Div
                            l10nKey="AboutDialog.BuiltOnDate"
                            l10nParam0={builtOnDate}
                        >
                            Built on {0}
                        </Div>
                    </div>
                </div>
                <div
                    css={css`
                        font-size: 8pt;
                        width: 350px;
                        padding: 0px 10px;
                        overflow-y: scroll; // This always shows a scrollbar.  'auto' only displays one when needed.
                        background-color: #ffffff;

                        a {
                            color: ${kBloomBlue};
                        }
                        h3 {
                            font-size: 14pt;
                            font-weight: bold;
                        }
                        p,
                        li {
                            text-align: left;
                            margin-top: 6px;
                        }
                        ul,
                        li {
                            list-style-type: none;
                            padding-left: 20px;
                        }
                    `}
                >
                    <h3>
                        Copyright 2011-{new Date().getFullYear()}{" "}
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                    </h3>
                    <h3>License</h3>
                    <p>
                        Open source (
                        <a
                            href="https://sil.mit-license.org/"
                            target="_blank"
                            rel="noreferrer"
                        >
                            MIT License
                        </a>
                        ).
                    </p>
                    <h3>Credits</h3>
                    <p>
                        Noel Chou (
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ): Programming
                    </p>
                    <br></br>
                    <p>
                        John Hatton (
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ): Program Manager, UX Design, Programming
                    </p>
                    <br></br>
                    <p>
                        Marlon Hovland (
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ): Documentation
                    </p>
                    <br></br>
                    <p>
                        Stephen McConnel (
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ): Programming
                    </p>
                    <br></br>
                    <p>
                        Andrew Polk (
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ): Dev Lead, Programming
                    </p>
                    <br></br>
                    <p>
                        Colin Suggett (
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ): Testing
                    </p>
                    <br></br>
                    <p>
                        John Thomson (
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ): Programming
                    </p>
                    <br></br>
                    <p>
                        Past contributors: Eberhard Beilharz, Jeremy Brown, Rick
                        Conrad, Bruce Cox, Wendy Gale, Phil Hopper, Susanna
                        Imrie, Gordon Martin, David Moore, Sue Newland, Marvin
                        Nichols, Dirk Sprenger, Len Wallstrom.
                    </p>
                    <h3>
                        Bloom relies on the following open source works of
                        others:
                    </h3>
                    <ul>
                        <li>
                            <a
                                href="https://github.com/autofac/Autofac"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Autofac
                            </a>
                        </li>
                        <li>
                            <a
                                href="http://ckeditor.com/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                CKEditor
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://github.com/statianzo/Fleck"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Fleck
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://ffmpeg.org/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                ffmpeg
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://www.ghostscript.com/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Ghostscript
                            </a>
                        </li>
                        <li>
                            <a
                                href="http://www.graphicsmagick.org/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                GraphicsMagick
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://github.com/KevinSheedy/jquery.alphanum"
                                target="_blank"
                                rel="noreferrer"
                            >
                                jquery.alphanum
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://www.newtonsoft.com/json"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Json.NET
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://lame.sourceforge.io/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                LAME
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://github.com/quentint/long-press"
                                target="_blank"
                                rel="noreferrer"
                            >
                                LongPress
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://github.com/naudio/NAudio"
                                target="_blank"
                                rel="noreferrer"
                            >
                                NAudio
                            </a>
                        </li>
                        <li>
                            <a
                                href="http://pdfsharp.com/PDFsharp/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                PdfSharp
                            </a>
                            : empira Software GmbH
                        </li>
                        <li>
                            PortableDevice library from{" "}
                            <a
                                href="https://github.com/derekwilson/PodcastUtilities"
                                target="_blank"
                                rel="noreferrer"
                            >
                                PodcastUtilities
                            </a>
                            , Andrew Trevarrow and Derek Wilson (
                            <a
                                href="https://github.com/BloomBooks/PodcastUtilities/blob/master/LICENSE.txt"
                                target="_blank"
                                rel="noreferrer"
                            >
                                license
                            </a>
                            )
                        </li>
                        <li>
                            <a
                                href="https://github.com/readium/readium-js"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Readium
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://restsharp.dev/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                RestSharp
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://github.com/icsharpcode/SharpZipLib"
                                target="_blank"
                                rel="noreferrer"
                            >
                                SharpZipLib
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://www.sqlite.org/index.html"
                                target="_blank"
                                rel="noreferrer"
                            >
                                SQLite
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://github.com/praeclarum/sqlite-net"
                                target="_blank"
                                rel="noreferrer"
                            >
                                sqlite-net
                            </a>
                        </li>
                        <li>
                            <a
                                href="http://call.canil.ca"
                                target="_blank"
                                rel="noreferrer"
                            >
                                SynPhony
                            </a>
                            : Norbert Rennert (SIL Global)
                        </li>
                        <li>
                            <a
                                href="https://github.com/mono/taglib-sharp"
                                target="_blank"
                                rel="noreferrer"
                            >
                                TagLibSharp
                            </a>
                        </li>
                        <li>
                            <a
                                href="https://github.com/CodeSeven/toastr"
                                target="_blank"
                                rel="noreferrer"
                            >
                                toastr
                            </a>
                        </li>
                        <li>
                            {/* <!--a href="http://webfx.eae.net/" target="_blank" rel="noreferrer"-->*/}
                            WebFX{/*<!--/a DEFUNCT LINK-->*/}: Erik Arvidsson
                        </li>
                    </ul>
                    <h3>Thanks</h3>
                    <p>
                        To our colleagues who invested time in giving feedback
                        and encouragement <em>before</em> Bloom became famous
                        :-)
                    </p>
                    <br></br>
                    <p>
                        Michael Cochran, Jon Coombs, David Coward, Paul Frank (
                        <a
                            href="http://www.sil-lead.org/"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL LEAD
                        </a>
                        ) Suzanne Hatton, Hannes Hirzel, Cambell Prince, Glenys
                        Waters, Payap University Linguistics Institute (Chiang
                        Mai, Thailand)
                    </p>
                    <br></br>
                    <p>
                        <a
                            href="http://BrowserStack.com"
                            target="_blank"
                            rel="noreferrer"
                        >
                            BrowserStack
                        </a>
                        , for a free open-source subscription to their{" "}
                        <strong>amazing</strong> multi-browser, multi-platform
                        testing service.
                    </p>
                    <br></br>
                    <p>
                        <a
                            href="https://zulip.com"
                            target="_blank"
                            rel="noreferrer"
                        >
                            Zulip
                        </a>
                        , where we keep in touch all day even though we are
                        distributed all over.
                    </p>
                    <h3>Image Credits</h3>
                    <p>
                        "USB" icon designed Dara Ullrich from The Noun Project
                    </p>
                    <br></br>
                    <p>"Blind" by Bluu from the Noun Project</p>
                    <br></br>
                    <p>
                        Earth icon designed by Francesco Paleari from The Noun
                        Project
                    </p>
                    <br></br>
                    <p>
                        Network icon designed by Stephen Boak from The Noun
                        Project
                    </p>
                    <br></br>
                    <p>
                        Leveled Reader template icon (Dashboard) by Chris Kerr
                        from The Noun Project
                    </p>
                    <br></br>
                    <p>
                        Decodable Reader template icon (Key) by William J.
                        Salvador from The Noun Project
                    </p>
                    <br></br>
                    <p>Refresh icon from Material Design Icons collection</p>
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCloseButton onClick={closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

let show: () => void = () => {};

export function showAboutDialog() {
    ShowEditViewDialog(<AboutDialogLauncher />);
    show();
}
