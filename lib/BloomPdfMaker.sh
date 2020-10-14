#!/bin/sh

# BloomPdfMaker.exe quit getting the environment from Bloom 4.9,
# so this shell script is needed to set up the environment for it.
# See https://issues.bloomlibrary.org/youtrack/issue/BL-9069.

LIB="$(dirname $0)"
BASE="$(dirname $LIB)"

if [ "$BASE" = "/usr/lib" ];
then
    RUNMODE=INSTALLED
    SHARE=$(echo $LIB | sed s=/lib/=/share/=)
else
    RUNMODE=DEVELOPER
    SHARE=$(dirname $BASE)
fi

# This information may help if something goes wrong...
echo BloomPdfMaker.sh: LIB=$LIB, BASE=$BASE, SHARE=$SHARE, RUNMODE=$RUNMODE

cd "$SHARE"
. ./environ
cd "$OLDPWD"

exec /opt/mono5-sil/bin/mono --debug "$LIB/BloomPdfMaker.exe" "$@"
