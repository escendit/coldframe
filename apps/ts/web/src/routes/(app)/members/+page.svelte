<script lang="ts">
  import type { SubmitFunction } from '@sveltejs/kit';
  import { enhance } from '$app/forms';
  import { invalidateAll } from '$app/navigation';
  import Button from '$lib/components/Button.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import Modal from '$lib/components/Modal.svelte';
  import MemberTile from '$lib/components/MemberTile.svelte';
  import RetryNotice from '$lib/components/RetryNotice.svelte';
  import SegmentedChoice from '$lib/components/SegmentedChoice.svelte';
  import TextInput from '$lib/components/TextInput.svelte';
  import { announce } from '$lib/announcer.svelte';
  import { t, type MessageKey } from '$lib/i18n';
  import {
    displayNameOf,
    inviteNoticeOf,
    inviteRoles,
    isLastOwner,
    memberChangedNoticeOf,
    memberChangeNoticeOf,
    membersNoticeOf,
    roleDescription,
    type EmailError,
    type InviteFailure,
    type InviteSuccess,
    type MemberChangeAction,
    type MemberChangeFailure,
    type MemberChangeSuccess,
  } from '$lib/members';
  import type { Role } from '$lib/roles';
  import { roleLabel } from '$lib/sites';
  import type { PageProps } from './$types';

  let { data, form }: PageProps = $props();

  const site = $derived(data.currentSite);
  /** The answer of the Invite form, or of a Role change or removal (Story 9.2), told apart by `action`. */
  const inviteAnswer: InviteSuccess | InviteFailure | null = $derived(form === null || form.action !== undefined ? null : (form as InviteSuccess | InviteFailure));
  const changeAnswer: MemberChangeSuccess | MemberChangeFailure | null = $derived(form?.action !== undefined ? (form as MemberChangeSuccess | MemberChangeFailure) : null);
  const failed: InviteFailure | null = $derived(inviteAnswer === null || 'done' in inviteAnswer ? null : inviteAnswer);
  const sentTo: string | null = $derived(inviteAnswer !== null && 'done' in inviteAnswer ? inviteAnswer.email : null);
  const changeFailed: MemberChangeFailure | null = $derived(changeAnswer === null || 'done' in changeAnswer ? null : changeAnswer);
  const changeDone: MemberChangeSuccess | null = $derived(changeAnswer !== null && 'done' in changeAnswer ? changeAnswer : null);
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

  /**
   * Role change and removal (UX-DR73): the tile an Owner taps opens the editor. Until a tile is tapped the editor
   * follows the last answer: a refused change keeps it on that Member (except one who is gone), a saved one closes it.
   */
  let tapped = $state(false);
  let openId: string | null = $state(null);
  let editRole: Role | null = $state(null);
  let changing: MemberChangeAction | null = $state(null);
  let confirmOpen = $state(false);
  let removeForm: HTMLFormElement | undefined = $state();

  const editingId = $derived(tapped ? openId : changeFailed !== null && changeFailed.notice !== 'memberGone' ? changeFailed.userId : null);
  const editing = $derived(data.members.find((member) => member.userId === editingId && !isLastOwner(data.members, member)) ?? null);
  const editingName = $derived(editing === null ? '' : displayNameOf(editing));
  const refusedHere = $derived(changeFailed !== null && editing !== null && changeFailed.userId === editing.userId && !tapped);
  const editedRole: Role = $derived(editRole ?? (refusedHere && changeFailed?.action === 'changeRole' ? changeFailed.role : (editing?.role ?? 'Member')));
  /** The Server's `last-owner`: one line under the Role field, until another Role is picked. */
  const roleError = $derived(refusedHere && editRole === null && changeFailed?.notice === 'lastOwner' ? t('members.lastOwner') : null);

  const changeNotice = $derived.by(() => {
    if (site === null) {
      return null;
    }
    if (changeDone !== null) {
      return { message: t(memberChangedNoticeOf(changeDone.action), { name: changeDone.name, role: t(roleLabel[changeDone.role]), siteName: changeDone.siteName }), tryAgain: false };
    }
    if (changeFailed === null || changeFailed.notice === 'lastOwner') {
      return null;
    }
    const { message, tryAgain } = memberChangeNoticeOf(changeFailed.notice);
    return { message: t(message, { name: changeFailed.name, siteName: changeFailed.siteName }), tryAgain };
  });

  function select(userId: string): void {
    openId = editingId === userId ? null : userId;
    tapped = true;
    editRole = null;
  }

  function closeEditor(): void {
    openId = null;
    tapped = true;
    editRole = null;
  }

  function askRemove(): void {
    confirmOpen = true;
  }

  function confirmRemove(): void {
    confirmOpen = false;
    removeForm?.requestSubmit();
  }

  /**
   * A Role change or removal. Afterwards the editor follows the answer. On success update() runs the load again, so a
   * User who is no longer Owner lands on Garden (the page's load sends them); a Member who is gone reads everything
   * again, and the list reloads.
   */
  function changingMember(action: MemberChangeAction): SubmitFunction {
    return () => {
      changing = action;
      return async ({ result, update }) => {
        await update({ reset: false });
        changing = null;
        tapped = false;
        openId = null;
        editRole = null;
        const answer = result.type === 'success' || result.type === 'failure' ? (result.data as Partial<MemberChangeSuccess & MemberChangeFailure> | undefined) : undefined;
        if (result.type === 'failure' && answer?.notice === 'memberGone') {
          await invalidateAll();
        }
      };
    };
  }

  $effect(() => {
    if (changeNotice !== null) {
      announce(changeNotice.message, changeDone !== null ? 'polite' : 'assertive');
    } else if (roleError !== null) {
      announce(roleError, 'assertive');
    }
  });

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
          <li>
            <MemberTile
              {member}
              lastOwner={isLastOwner(data.members, member)}
              selected={editing?.userId === member.userId}
              controls="cf-member-editor"
              onselect={() => {
                select(member.userId);
              }}
            />
          </li>
        {/each}
      </ul>
    {/if}
    {#if changeNotice !== null}
      {#if changeNotice.tryAgain && changeFailed !== null}
        <!-- Try again sends the same change once more. -->
        <RetryNotice
          id="cf-member-notice"
          message={changeNotice.message}
          action="?/{changeFailed.action}"
          fields={{ siteId: site.id, siteName: site.name, userId: changeFailed.userId, name: changeFailed.name, role: changeFailed.role }}
          submitting={changingMember(changeFailed.action)}
        />
      {:else}
        <InlineNotice id="cf-member-notice" message={changeNotice.message} />
      {/if}
    {/if}
    {#if editing !== null}
      <!-- Role change and removal (UX-DR73), for the Owner only: the page exists for no one else. -->
      <section id="cf-member-editor" class="cf-members__editor" aria-labelledby="cf-member-editor-title">
        <h3 id="cf-member-editor-title" class="cf-members__editor-title">{t('members.editor', { name: editingName })}</h3>
        <form class="cf-members__edit" method="POST" action="?/changeRole" novalidate use:enhance={changingMember('changeRole')}>
          <input type="hidden" name="siteId" value={site.id} />
          <input type="hidden" name="siteName" value={site.name} />
          <input type="hidden" name="userId" value={editing.userId} />
          <input type="hidden" name="name" value={editingName} />
          <input type="hidden" name="role" value={editedRole} />
          <SegmentedChoice
            id="cf-member-role"
            label={t('members.role')}
            options={roleOptions}
            value={editedRole}
            helper={t(roleDescription[editedRole])}
            invalid={roleError}
            onchange={(value: Role) => {
              editRole = value;
            }}
          />
          <div class="cf-members__actions">
            <Button type="submit" label={t('members.saveRole')} working={changing === 'changeRole'} workingLabel={t('members.savingRole')} />
            <Button variant="secondary" label={t('members.remove')} working={changing === 'removeMember'} workingLabel={t('members.removing')} onclick={askRemove} />
            <Button variant="ghost" label={t('members.closeEditor')} onclick={closeEditor} />
          </div>
        </form>
        <!-- Remove confirms in a Modal naming the person and the Site; the Modal's action submits this form. -->
        <form class="cf-members__remove" method="POST" action="?/removeMember" bind:this={removeForm} use:enhance={changingMember('removeMember')}>
          <input type="hidden" name="siteId" value={site.id} />
          <input type="hidden" name="siteName" value={site.name} />
          <input type="hidden" name="userId" value={editing.userId} />
          <input type="hidden" name="name" value={editingName} />
          <input type="hidden" name="role" value={editing.role} />
        </form>
        <Modal
          id="cf-remove-member"
          bind:open={confirmOpen}
          title={t('members.removeQuestion', { name: editingName, siteName: site.name })}
          detail={t('members.removeDetail', { siteName: site.name })}
          actionLabel={t('members.removeAction')}
          onaction={confirmRemove}
        />
      </section>
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

  .cf-members__editor,
  .cf-members__edit {
    display: grid;
    gap: var(--cf-spacing-5);
  }

  .cf-members__editor {
    padding: var(--cf-spacing-5);
    border: 1px solid var(--cf-color-border-subtle);
  }

  .cf-members__editor-title {
    margin: 0;
    font-family: var(--cf-type-heading-compact-font-family, var(--cf-type-body-lg-font-family));
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
  }

  .cf-members__actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--cf-spacing-3);
  }

  .cf-members__invite {
    display: grid;
    gap: var(--cf-spacing-5);
  }
</style>
