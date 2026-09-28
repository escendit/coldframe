{{/* Resource names default to the release name. */}}
{{- define "ingress.fullname" -}}
{{- default .Release.Name .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "ingress.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: coldframe
{{- end -}}

{{/* The host of a component (web, api or auth): hosts.<component>, or its default below domain. */}}
{{- define "ingress.host" -}}
{{- $root := index . 0 -}}
{{- $component := index . 1 -}}
{{- $default := ternary $root.Values.domain (printf "%s.%s" $component $root.Values.domain) (eq $component "web") -}}
{{- default $default (get $root.Values.hosts $component) -}}
{{- end -}}

{{/* The three components, in the order of the Certificate's dnsNames and the Ingress rules. */}}
{{- define "ingress.components" -}}
web api auth
{{- end -}}

{{/*
Guards the issuer: without ACME, an external Issuer must be named, or the Certificate would
reference nothing.
*/}}
{{- define "ingress.validate" -}}
{{- if and (not .Values.acme.enabled) (not .Values.externalIssuer.name) -}}
{{- fail "acme.enabled is false and externalIssuer.name is empty: set externalIssuer.name to an Issuer you manage, or enable acme" -}}
{{- end -}}
{{- if not .Values.domain -}}
{{- fail "domain is empty: set the domain of the three hosts" -}}
{{- end -}}
{{- end -}}
