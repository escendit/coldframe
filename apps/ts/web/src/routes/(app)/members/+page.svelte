<script lang="ts">
  import type { SubmitFunction } from '@sveltejs/kit';
  import { enhance } from '$app/forms';
  import Button from '$lib/components/Button.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import MemberTile from '$lib/components/MemberTile.svelte';
  import RetryNotice from '$lib/components/RetryNotice.svelte';
  import SegmentedChoice from '$lib/components/SegmentedChoice.svelte';
  import TextInput from '$lib/components/TextInput.svelte';
  import { announce } from '$lib/announcer.svelte';
  import { t, type MessageKey } from '$lib/i18n';
  import { inviteNoticeOf, inviteRoles, membersNoticeOf, roleDescription, type EmailError, type InviteFailure } from '$lib/members';
  import type { Role } from '$lib/roles';
  import { roleLabel } from '$lib/sites';
  import type { PageProps } from './$types';

  let { data, form }: PageProps = $props();

  const site = $derived(data.currentSite);
  const failed: InviteFailure | null = $derived(form === null || 'done' in form ? null : form);
  const sentTo: string | null = $derived(form !== null && 'done' in form ? form.email : null);
  const listNotice = $derived(membersNoticeOf(data.membersNotice));

  let working = $state(false);
  /** The Role picked since the last answer; before that the refused attempt's Role, else Member. */
  let picked: Role | null = $state(null);
  const role: Role = $derived(picked ?? failed?.role ?? 'Member');

  const roleOptions = inviteRoles.map((value) => ({ value, label: t(roleLabel[value]) }));

  const emailReasons: Readonly<Record<EmailError, MessageKey>> = {
    blank: 'members.emailBlank',
    invalid: 'members.emailInvalid',
  };
  const emailError = $derived(failed?.emailError === null || failed?.emailError === undefined ? null : t(emailReasons[failed.emailError]));

  const notice = $derived.by(() => {
    if (sentTo !== null) {
      return { message: t('members.sent', { email: sentTo }), tryAgain: false };
    }
    if (failed?.notice === null || failed?.notice === undefined) {
      return null;
    }
    const { message, tryAgain } = inviteNoticeOf(failed.notice);
    return { message: t(message, { siteName: failed.siteName }), tryAgain };
  });

  /** A refusal keeps what was typed and picked; a sent invitation clears the form (UX-DR52). */
  const submitting: SubmitFunction = () => {
    working = true;
    return async ({ result, update }) => {
      await update({ reset: result.type === 'success' });
      working = false;
      picked = null;
    };
  };

  $effect(() => {
    if (sentTo !== null) {
      announce(t('members.sent', { email: sentTo }), 'polite');
    } else if (failed !== null) {
      const spoken = notice?.message ?? emailError;
      if (spoken !== null) {
        announce(spoken, 'assertive');
      }
    }
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('members.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('members.title')}</h1>

{#if site !== null}
  <!-- Members (UX-DR73): one tile per Membership; there are no pending invitations and no accept surface here. -->
  <section class="cf-members__section" aria-labelledby="cf-members-list">
    <h2 id="cf-members-list" class="cf-section-title">{t('members.list', { siteName: site.name })}</h2>
    {#if listNotice !== null}
      <InlineNotice id="cf-members-notice" message={t(listNotice.message)} action={listNotice.tryAgain ? { label: t('notice.tryAgain'), href: '/members', reload: true } : null} />
    {:else}
      <ul class="cf-members__list" aria-labelledby="cf-members-list">
        {#each data.members as member (member.userId)}
          <li><MemberTile {member} /></li>
        {/each}
      </ul>
    {/if}
  </section>

  <section class="cf-members__section" aria-labelledby="cf-members-invite">
    <h2 id="cf-members-invite" class="cf-section-title">{t('members.invite')}</h2>
    <!-- Invite form (UX-DR52). A sent invitation re-keys it, so it starts empty again. -->
    {#key form}
      <form class="cf-members__invite" method="POST" action="?/invite" novalidate use:enhance={submitting}>
        <input type="hidden" name="siteId" value={site.id} />
        <input type="hidden" name="siteName" value={site.name} />
        <input type="hidden" name="role" value={role} />
        <TextInput
          id="cf-invite-email"
          name="email"
          type="email"
          label={t('members.email')}
          helper={t('members.emailHelper')}
          value={failed?.email ?? ''}
          invalid={emailError}
          autocomplete="email"
        />
        <SegmentedChoice
          id="cf-invite-role"
          label={t('members.role')}
          options={roleOptions}
          value={role}
          helper={t(roleDescription[role])}
          onchange={(value: Role) => {
            picked = value;
          }}
        />
        {#if notice !== null && !notice.tryAgain}
          <InlineNotice id="cf-invite-notice" message={notice.message} />
        {/if}
        <div>
          <Button type="submit" label={t('members.sendInvite')} {working} workingLabel={t('members.sending')} />
        </div>
      </form>
    {/key}
    {#if notice !== null && notice.tryAgain && failed !== null}
      <!-- Try again sends the same address and Role once more. -->
      <RetryNotice
        id="cf-invite-notice"
        message={notice.message}
        action="?/invite"
        fields={{ siteId: site.id, siteName: site.name, email: failed.email, role: failed.role }}
        {submitting}
      />
    {/if}
  </section>
{/if}

<style>
  .cf-members__section {
    display: grid;
    gap: var(--cf-spacing-5);
    max-width: 40rem;
    margin-block-end: var(--cf-spacing-7);
  }

  .cf-members__list {
    display: grid;
    gap: var(--cf-spacing-3);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .cf-members__invite {
    display: grid;
    gap: var(--cf-spacing-5);
  }
</style>
