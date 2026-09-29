# The Coldframe web image: the SvelteKit backend-for-frontend on adapter-node.
#
# Build from the repository root:
#   docker buildx build -f apps/ts/web/Dockerfile -t coldframe/web:dev .
#
# The build stage runs on the build platform. Only the production deploy of @coldframe/web is
# resolved for the target architecture, so an arm64 image needs no emulation. The final stage
# only copies files. Configuration comes from environment variables only
# (deploy/images/README.md).

FROM --platform=$BUILDPLATFORM docker.io/library/node:24.21.0-trixie-slim AS build

ARG TARGETARCH
ARG PNPM_VERSION=12.6.0

ENV PNPM_HOME=/pnpm \
    CI=true
ENV PATH="${PNPM_HOME}:${PATH}"

RUN npm install --global --no-fund --no-audit "pnpm@${PNPM_VERSION}"

WORKDIR /src

COPY . .

# Build tooling runs on the build platform, so the workspace install uses its architecture.
RUN --mount=type=cache,id=coldframe-pnpm-store,target=/pnpm/store \
    pnpm config set store-dir /pnpm/store --location project \
    && pnpm install --frozen-lockfile

RUN pnpm --filter @coldframe/web build

# The production deploy is resolved for the target architecture: pnpm installs the native
# optional packages for that CPU instead of the build platform's.
RUN --mount=type=cache,id=coldframe-pnpm-store,target=/pnpm/store \
    case "${TARGETARCH}" in \
      amd64) cpu=x64 ;; \
      arm64) cpu=arm64 ;; \
      *) echo "Unsupported TARGETARCH '${TARGETARCH}'." >&2; exit 1 ;; \
    esac \
    && printf '\nsupportedArchitectures:\n  os: [linux]\n  cpu: [%s]\n  libc: [glibc]\n' "${cpu}" >> pnpm-workspace.yaml \
    && pnpm --filter @coldframe/web deploy --prod /out

FROM docker.io/library/node:24.21.0-trixie-slim

WORKDIR /app

COPY --from=build /out/package.json ./package.json
COPY --from=build /out/node_modules/ ./node_modules/
COPY --from=build /out/build/ ./build/

ENV NODE_ENV=production \
    PORT=3000
EXPOSE 3000

# The numeric UID of the image's "node" user, so Kubernetes runAsNonRoot can verify it.
USER 1000

ENTRYPOINT ["node", "build"]
