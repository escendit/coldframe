{{/* Resource names default to the release name. */}}
{{- define "keycloak.fullname" -}}
{{- default .Release.Name .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "keycloak.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: coldframe
{{- end -}}

{{- define "keycloak.selectorLabels" -}}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{/* "<repository>:<tag>", the tag defaulting to the chart's appVersion. Call with (list $ .Values.image). */}}
{{- define "keycloak.image" -}}
{{- $root := index . 0 -}}
{{- $image := index . 1 -}}
{{- printf "%s:%s" $image.repository (default $root.Chart.AppVersion $image.tag) -}}
{{- end -}}

{{- define "keycloak.podSecurityContext" -}}
runAsNonRoot: true
runAsUser: {{ int .Values.runAsUser }}
runAsGroup: {{ int .Values.runAsUser }}
seccompProfile:
  type: RuntimeDefault
{{- end -}}

{{- define "keycloak.containerSecurityContext" -}}
allowPrivilegeEscalation: false
capabilities:
  drop:
    - ALL
{{- end -}}
