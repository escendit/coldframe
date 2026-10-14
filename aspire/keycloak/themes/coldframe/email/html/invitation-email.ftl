<#-- Phase Two's invitation email (keycloak-orgs 0.183, InvitationsResource.sendInvitationEmail).
     Message arguments: {0} email, {1} realm, {2} Site (Organization display name), {3} inviter, {4} link. -->
<#-- The Site name is the Owner's own text: escaped here, so no markup of it reaches kcSanitize. -->
<#assign siteName = orgName?replace("&", "&amp;")?replace("<", "&lt;")?replace(">", "&gt;")?replace("\"", "&quot;")>
<#import "template.ftl" as layout>
<@layout.emailLayout>
${kcSanitize(msg("invitationEmailBodyHtml", email, realmName, siteName, inviterName, link))?no_esc}
</@layout.emailLayout>
