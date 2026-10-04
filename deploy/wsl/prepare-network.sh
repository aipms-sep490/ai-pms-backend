#!/bin/sh
set -eu

docker network inspect go-exe_goexe-net >/dev/null
docker inspect mssql >/dev/null
if ! docker network inspect aipms-data >/dev/null 2>&1; then
    docker network create aipms-data >/dev/null
fi
if ! docker inspect mssql --format '{{json .NetworkSettings.Networks}}' | grep -q '"aipms-data"'; then
    docker network connect aipms-data mssql
fi
echo 'Network ready: existing mssql remains attached to its original networks.'
