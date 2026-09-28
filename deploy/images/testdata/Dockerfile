# Fixture for deploy/images/test.sh: the final stage is the last FROM.
FROM --platform=$BUILDPLATFORM example.org/build:1.0 AS build
RUN true

from --platform=linux/amd64 example.org/final:2.0 AS final
COPY --from=build /out /app
