import { css } from "@emotion/react";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "./BloomDialog/BloomDialog";
import { DialogCloseButton } from "./BloomDialog/commonDialogComponents";
import { useL10n } from "./l10nHooks";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { get, getBloomApiPrefix } from "../utils/bloomApi";
import { useEffect, useState } from "react";
import { Div } from "./l10nComponents";

export const AboutDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    const cssh3 = `font-size: 14pt;
                  font-weight: bold;`;
    const cssbody = `font-family: arial;
                    font-size: 8pt;
                    width: 350px;`; // 335px
    const cssp = `text-align: left;
                 margin-top: 6px;`;
    const cssul = `list-style-type: none;
                  padding-left: 20px;`; // 0px
    const cssli = cssp + cssul;

    const logos = [
        getBloomApiPrefix(false) +
            "images/SIL Glyph Logo Color - Abbysinica RGB.png",
        getBloomApiPrefix(false) +
            "images/SIL Glyph Logo Color - Andika v1 RGB.png",
        getBloomApiPrefix(false) +
            "images/SIL Glyph Logo Color - Andika v2 RGB.png",
        getBloomApiPrefix(false) +
            "images/SIL Glyph Logo Color - Annapurna RGB.png",
        getBloomApiPrefix(false) +
            "images/SIL Glyph Logo Color - Tai Heritage Pro RGB.png"
    ];

    function logoURI(logos: string[]) {
        return logos[Math.floor(Math.random() * logos.length)];
    }

    const [buildNumber, setBuildNumber] = useState<string | undefined>(
        undefined
    );

    var useFullVersionNumber = false;

    useEffect(() => {
        get(
            useFullVersionNumber
                ? "app/fullVersionNumber"
                : "app/shortVersionNumber",
            result => {
                setBuildNumber(result.data);
            }
        );
    }, []);

    const [builtOnDate, setBuiltOnDate] = useState<string | undefined>(
        undefined
    );

    useEffect(() => {
        get("app/versionBuildDate", result => {
            setBuiltOnDate(result.data);
        });
    }, []);

    show = showDialog;

    return (
        <BloomDialog
            {...propsForBloomDialog}
            css={css`
                height: 555px;
                background-color: #f5f5f5;
            `}
        >
            {/* This file needs to be kept in sync with its localized versions (e.g. aboutBox-fr.htm). */}

            <DialogTitle
                title={useL10n(
                    "About {0}",
                    "AboutDialog.AboutBloom",
                    "Dialog Title",
                    "Bloom"
                )}
            />
            <DialogMiddle
                css={css`
                    display: grid;
                    grid-template-columns: 150px auto; // Logo only needs specific amount of space
                `}
            >
                <div
                    css={css`
                        display: grid;
                        align-content: space-between;
                        padding-right: 10px;
                    `}
                >
                    <div>
                        <br></br>
                        <img src={logoURI(logos)} width="134" />
                    </div>
                    <div>
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
                        ${cssbody}
                        padding-left: 10px;
                        overflow-y: scroll; // This always shows a scrollbar.  'auto' only displays one when needed.
                        background-color: #ffffff;
                    `}
                >
                    <h3
                        css={css`
                            ${cssh3}
                        `}
                    >
                        {/* When updating the year, also update LICENSE.txt */}
                        Copyright 2011-2024&nbsp;
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                    </h3>
                    <h3
                        css={css`
                            ${cssh3}
                        `}
                    >
                        License
                    </h3>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
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
                    <h3
                        css={css`
                            ${cssh3}
                        `}
                    >
                        Credits
                    </h3>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Noel Chou&nbsp;(
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ):&nbsp;Programming
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        John Hatton&nbsp;(
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ):&nbsp;Program Manager, UX Design, Programming
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Marlon Hovland&nbsp;(
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ):&nbsp;Documentation
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Stephen McConnel&nbsp;(
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ):&nbsp;Programming
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Andrew Polk&nbsp;(
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ):&nbsp;Dev Lead, Programming
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Colin Suggett&nbsp;(
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ):&nbsp;Testing
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        John Thomson&nbsp;(
                        <a
                            href="https://sil.org"
                            target="_blank"
                            rel="noreferrer"
                        >
                            SIL Global
                        </a>
                        ):&nbsp;Programming
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Past contributors: Eberhard Beilharz, Jeremy Brown, Rick
                        Conrad, Bruce Cox, Wendy Gale, Phil Hopper, Susanna
                        Imrie, Gordon Martin, David Moore, Sue Newland, Marvin
                        Nichols, Dirk Sprenger, Len Wallstrom.
                    </p>
                    <h3
                        css={css`
                            ${cssh3}
                        `}
                    >
                        Bloom relies on the following open source works of
                        others:
                    </h3>
                    <ul
                        css={css`
                            ${cssul}
                        `}
                    >
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/autofac/Autofac"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Autofac
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="http://ckeditor.com/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                CKEditor
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/statianzo/Fleck"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Fleck
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://ffmpeg.org/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                ffmpeg
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://www.ghostscript.com/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Ghostscript
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="http://www.graphicsmagick.org/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                GraphicsMagick
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/KevinSheedy/jquery.alphanum"
                                target="_blank"
                                rel="noreferrer"
                            >
                                jquery.alphanum
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://www.newtonsoft.com/json"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Json.NET
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://lame.sourceforge.io/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                LAME
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="http://www.html-tidy.org/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                LibTidy
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/quentint/long-press"
                                target="_blank"
                                rel="noreferrer"
                            >
                                LongPress
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://www.toptensoftware.com/markdowndeep/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                MarkdownDeep
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/naudio/NAudio"
                                target="_blank"
                                rel="noreferrer"
                            >
                                NAudio
                            </a>
                        </li>
                        {/* why do we need to credit a test framework? */}
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://nunit.org/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                NUnit
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="http://pdfsharp.com/PDFsharp/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                PdfSharp
                            </a>
                            : empira Software GmbH
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            PortableDevice library from&nbsp;
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
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/readium/readium-js"
                                target="_blank"
                                rel="noreferrer"
                            >
                                Readium
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://restsharp.dev/"
                                target="_blank"
                                rel="noreferrer"
                            >
                                RestSharp
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/icsharpcode/SharpZipLib"
                                target="_blank"
                                rel="noreferrer"
                            >
                                SharpZipLib
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://www.sqlite.org/index.html"
                                target="_blank"
                                rel="noreferrer"
                            >
                                SQLite
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/praeclarum/sqlite-net"
                                target="_blank"
                                rel="noreferrer"
                            >
                                sqlite-net
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/squirrel/squirrel.windows"
                                target="_blank"
                                rel="noreferrer"
                            >
                                squirrel
                            </a>
                            : Paul Betts
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="http://call.canil.ca"
                                target="_blank"
                                rel="noreferrer"
                            >
                                SynPhony
                            </a>
                            : Norbert Rennert (SIL Global)
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/mono/taglib-sharp"
                                target="_blank"
                                rel="noreferrer"
                            >
                                TagLibSharp
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/markbeaton/TidyManaged"
                                target="_blank"
                                rel="noreferrer"
                            >
                                TidyManaged
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            <a
                                href="https://github.com/CodeSeven/toastr"
                                target="_blank"
                                rel="noreferrer"
                            >
                                toastr
                            </a>
                        </li>
                        <li
                            css={css`
                                ${cssli}
                            `}
                        >
                            {/* <!--a href="http://webfx.eae.net/" target="_blank" rel="noreferrer"-->*/}
                            WebFX{/*<!--/a DEFUNCT LINK-->*/}: Erik Arvidsson
                        </li>
                    </ul>
                    <h3
                        css={css`
                            ${cssh3}
                        `}
                    >
                        Thanks
                    </h3>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        To our colleagues who invested time in giving feedback
                        and encouragement <em>before</em> Bloom became famous
                        :-)
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
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
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        <a
                            href="http://BrowserStack.com"
                            target="_blank"
                            rel="noreferrer"
                        >
                            BrowserStack
                        </a>
                        , for a free open-source subscription to their&nbsp;
                        <strong>amazing</strong> multi-browser, multi-platform
                        testing service.
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
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
                    <h3
                        css={css`
                            ${cssh3}
                        `}
                    >
                        Image Credits
                    </h3>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        "USB" icon designed Dara Ullrich from The Noun Project
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        "Blind" by Bluu from the Noun Project
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Earth icon designed by Francesco Paleari from The Noun
                        Project
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Network icon designed by Stephen Boak from The Noun
                        Project
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Leveled Reader template icon (Dashboard) by Chris Kerr
                        from The Noun Project
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Decodable Reader template icon (Key) by William J.
                        Salvador from The Noun Project
                    </p>
                    <br></br>
                    <p
                        css={css`
                            ${cssp}
                        `}
                    >
                        Refresh icon from Material Design Icons collection
                    </p>
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCloseButton onClick={closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

let show: () => void = () => {
    window.alert("LanguageChooserDialog is not set up yet.");
};

WireUpForWinforms(AboutDialog);
