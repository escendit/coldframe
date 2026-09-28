# Keycloak: the Phase Two distribution plus keycloak-temporal-extensions. The local Aspire stack
# builds it, and each release publishes it as ghcr.io/escendit/coldframe/keycloak (Story 2.2).
#
# Build from the repository root:
#   docker buildx build -f aspire/keycloak/Dockerfile -t coldframe/keycloak:dev .
#
# The extension publishes no release assets and its Maven package needs a token, so it is
# built here from the public source. Everything below comes from a public registry or
# repository without credentials (AD-15).
#
# The build stages run on the build platform: the extension and the Quarkus build output are
# plain Java, the same on every architecture. The final stage only copies files, so an arm64
# image needs no emulation (deploy/images/README.md).
#
# The extension must be re-verified against the Keycloak version whenever either one changes.
# The Keycloak version appears twice below (build stage and final stage): change both.

ARG MAVEN_VERSION=3.9.16-eclipse-temurin-25-noble

FROM --platform=$BUILDPLATFORM docker.io/library/maven:${MAVEN_VERSION} AS extension

ARG EXTENSION_REPOSITORY=https://github.com/escendit/keycloak-temporal-extensions.git
ARG EXTENSION_TAG=v0.0.1-rc.2
# The commit the tag pointed to when it was verified. The build fails if the tag moves.
ARG EXTENSION_COMMIT=63f89f2db9c71f93d58f900867afc00426d012f6
ARG MAVEN_DEPENDENCY_PLUGIN_VERSION=3.11.0

WORKDIR /opt/build

RUN git clone --depth 1 --branch "${EXTENSION_TAG}" "${EXTENSION_REPOSITORY}" . \
    && test "$(git rev-parse HEAD)" = "${EXTENSION_COMMIT}"

RUN mvn --batch-mode --no-transfer-progress package -DskipTests

# The JAR is not shaded: the Temporal SDK and its runtime dependencies go to providers/ with it.
# Keycloak already ships its own classes, logging, Jackson and Micrometer.
RUN mvn --batch-mode --no-transfer-progress \
    "org.apache.maven.plugins:maven-dependency-plugin:${MAVEN_DEPENDENCY_PLUGIN_VERSION}:copy-dependencies" \
    -DoutputDirectory=/opt/build/providers \
    -DexcludeScope=provided \
    -DexcludeGroupIds=org.keycloak,org.jboss.logging \
    -DexcludeArtifactIds=jackson-databind,jackson-core,jackson-annotations,jackson-datatype-jdk8,micrometer-core \
    && cp target/listener-temporal-*.jar /opt/build/providers/

# The Quarkus build ("kc.sh build") bakes the providers and the build-time options into
# /opt/keycloak; the final stage copies the result.
FROM --platform=$BUILDPLATFORM quay.io/phasetwo/phasetwo-keycloak:26.6.7 AS build

COPY --from=extension /opt/build/providers/*.jar /opt/keycloak/providers/

# Build-time options. "start --optimized" expects them to match at run time.
ENV KC_DB=postgres
ENV KC_HEALTH_ENABLED=true
ENV KC_METRICS_ENABLED=true

RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/phasetwo/phasetwo-keycloak:26.6.7

# The base image's "keycloak" user (UID and GID 2000) owns /opt/keycloak.
COPY --from=build --chown=2000:2000 /opt/keycloak/ /opt/keycloak/

ENV KC_DB=postgres
ENV KC_HEALTH_ENABLED=true
ENV KC_METRICS_ENABLED=true

# HTTP, HTTPS, management (health and metrics).
EXPOSE 8080 8443 9000

# The numeric UID of the image's "keycloak" user, so Kubernetes runAsNonRoot can verify it.
USER 2000

ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
