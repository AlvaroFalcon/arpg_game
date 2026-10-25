#!/bin/bash
# cmp.sh <refdir> <dir>: AE at fuzz 2% per png, percent of pixels
for f in "$2"/*.png; do n=$(basename "$f"); [ -f "$1/$n" ] || { echo "$n: no ref"; continue; }
tot=$(identify -format "%[fx:w*h]" "$f"); ae=$(compare -metric AE -fuzz 2% "$1/$n" "$f" /dev/null 2>&1)
echo "$n: $ae ($(python3 -c "print(round(100*$ae/$tot,3))")%)"; done
