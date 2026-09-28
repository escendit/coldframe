{{/* The CNPG Cluster name; its read-write Service is "<name>-rw". */}}
{{- define "database.clusterName" -}}
{{- .Values.clusterName | trunc 50 | trimSuffix "-" -}}
{{- end -}}

{{/* The ObjectStore that holds the Barman Cloud configuration. */}}
{{- define "database.objectStoreName" -}}
{{- printf "%s-backup" (include "database.clusterName" .) -}}
{{- end -}}

{{/* The archive folder this cluster writes to: backup.serverName, defaulting to the cluster name. */}}
{{- define "database.serverName" -}}
{{- default (include "database.clusterName" .) .Values.backup.serverName -}}
{{- end -}}

{{/* The archive folder a restore reads from: recovery.sourceServerName, defaulting to the cluster name. */}}
{{- define "database.sourceServerName" -}}
{{- default (include "database.clusterName" .) .Values.recovery.sourceServerName -}}
{{- end -}}

{{- define "database.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: coldframe
{{- end -}}

{{/*
Guards recovery mode: the restored cluster must archive to a new folder. Barman refuses a
non-empty archive, and sharing the source's folder would corrupt its timeline.
*/}}
{{- define "database.validate" -}}
{{- if and .Values.recovery.enabled (eq (include "database.serverName" .) (include "database.sourceServerName" .)) -}}
{{- fail (printf "recovery.enabled: backup.serverName (%s) must differ from recovery.sourceServerName (%s); the restored cluster must archive to a new folder" (include "database.serverName" .) (include "database.sourceServerName" .)) -}}
{{- end -}}
{{- end -}}
