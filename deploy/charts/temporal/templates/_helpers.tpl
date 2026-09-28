{{/* Resource names default to the release name. */}}
{{- define "temporal.fullname" -}}
{{- default .Release.Name .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "temporal.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: coldframe
{{- end -}}

{{- define "temporal.selectorLabels" -}}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{/* "<repository>:<tag>", the tag defaulting to the chart's appVersion. Call with (list $ .Values.image). */}}
{{- define "temporal.image" -}}
{{- $root := index . 0 -}}
{{- $image := index . 1 -}}
{{- printf "%s:%s" $image.repository (default $root.Chart.AppVersion $image.tag) -}}
{{- end -}}

{{- define "temporal.podSecurityContext" -}}
runAsNonRoot: true
runAsUser: {{ int .Values.runAsUser }}
runAsGroup: {{ int .Values.runAsUser }}
seccompProfile:
  type: RuntimeDefault
{{- end -}}

{{- define "temporal.containerSecurityContext" -}}
allowPrivilegeEscalation: false
capabilities:
  drop:
    - ALL
{{- end -}}

{{/* The database credentials, from the coldframe-db-temporal Secret. */}}
{{- define "temporal.databaseCredentials" -}}
- name: {{ index . 0 }}
  valueFrom:
    secretKeyRef:
      name: coldframe-db-temporal
      key: username
- name: {{ index . 1 }}
  valueFrom:
    secretKeyRef:
      name: coldframe-db-temporal
      key: password
{{- end -}}
