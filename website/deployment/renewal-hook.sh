#!/bin/sh
set -eu
if [ "${RENEWED_LINEAGE:-}" = /etc/letsencrypt/live/fruitsatelier.himiko.moe ]; then
    /usr/sbin/nginx -t
    /usr/bin/systemctl reload nginx
fi
