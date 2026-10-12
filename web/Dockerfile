# syntax=docker/dockerfile:1
# Build from the web folder: docker build web

FROM node:24-alpine AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

# nginx that runs as a non-root user and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.27-alpine
# API_UPSTREAM is where /api and /health are proxied (the API container in Compose). Filled in at start-up by the
# image's template step (envsubst).
ENV API_UPSTREAM=http://api:8080
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /web/dist/web/browser /usr/share/nginx/html
EXPOSE 8080
