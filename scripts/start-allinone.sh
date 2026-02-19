#!/usr/bin/env bash
set -euo pipefail

ASPNETCORE_URLS=${ASPNETCORE_URLS:-http://0.0.0.0:8080}
FRONTEND_PORT=${FRONTEND_PORT:-3000}

export ASPNETCORE_URLS
export PORT=$FRONTEND_PORT
export HOSTNAME=0.0.0.0

# Start backend (logs to stdout/stderr for docker)
dotnet /app/backend/InfraLLM.Api.dll &
BACKEND_PID=$!

# Start frontend (logs to stdout/stderr for docker)
node /app/frontend/server.js &
FRONTEND_PID=$!

# Wait a moment for backend/frontend to start listening
sleep 2

# Start nginx in foreground (PID 1 behavior)
cleanup() {
  kill $BACKEND_PID $FRONTEND_PID 2>/dev/null || true
  nginx -s quit 2>/dev/null || true
}
trap cleanup SIGTERM SIGINT

nginx -g 'daemon off;' &
NGINX_PID=$!

# Wait on any process — if one dies, exit
wait -n $BACKEND_PID $FRONTEND_PID $NGINX_PID
