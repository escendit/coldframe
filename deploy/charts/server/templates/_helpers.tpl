{{/* Resource names default to the release name. */}}
{{- define "server.fullname" -}}
{{- default .Release.Name .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "server.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: coldframe
{{- end -}}

{{- define "server.selectorLabels" -}}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{/* "<repository>:<tag>", the tag defaulting to the chart's appVersion. Call with (list $ .Values.image). */}}
{{- define "server.image" -}}
{{- $root := index . 0 -}}
{{- $image := index . 1 -}}
{{- printf "%s:%s" $image.repository (default $root.Chart.AppVersion $image.tag) -}}
{{- end -}}

{{- define "server.podSecurityContext" -}}
runAsNonRoot: true
runAsUser: {{ int .Values.runAsUser }}
runAsGroup: {{ int .Values.runAsUser }}
seccompProfile:
  type: RuntimeDefault
{{- end -}}

{{- define "server.containerSecurityContext" -}}
allowPrivilegeEscalation: false
capabilities:
  drop:
    - ALL
{{- end -}}

{{/*
The Server database connection string, composed by Kubernetes from the coldframe-db-server
Secret ($(VAR) expansion). The password must not contain ";".
*/}}
{{- define "server.databaseEnv" -}}
- name: DB_USERNAME
  valueFrom:
    secretKeyRef:
      name: coldframe-db-server
      key: username
- name: DB_PASSWORD
  valueFrom:
    secretKeyRef:
      name: coldframe-db-server
      key: password
- name: ConnectionStrings__coldframe
  value: {{ printf "Host=%s;Port=%d;Database=%s;Username=$(DB_USERNAME);Password=$(DB_PASSWORD)" .Values.database.host (int .Values.database.port) .Values.database.name | quote }}
{{- end -}}
