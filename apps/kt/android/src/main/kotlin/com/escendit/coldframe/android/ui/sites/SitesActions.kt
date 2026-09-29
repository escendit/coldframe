package com.escendit.coldframe.android.ui.sites

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.sites.FirstRunStep
import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteMenuAction
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesNotice
import com.escendit.coldframe.core.sites.StepState
import com.escendit.coldframe.core.sites.availableTimeZones

/**
 * What the Sites surfaces can ask of the core. The shell never computes Sites state; it forwards
 * taps to [SitesEngine] and renders what comes back (AD-14).
 */
class SitesActions(
    val load: () -> Unit = {},
    val select: (String) -> Unit = {},
    val newSite: () -> Unit = {},
    val cancelNewSite: () -> Unit = {},
    val setName: (String) -> Unit = {},
    val confirmTimeZone: () -> Unit = {},
    val changeTimeZone: () -> Unit = {},
    val pickTimeZone: (String) -> Unit = {},
    val submit: () -> Unit = {},
    val timeZones: () -> List<String> = { emptyList() },
) {
    companion object {
        val None = SitesActions()

        fun of(engine: SitesEngine): SitesActions =
            SitesActions(
                load = engine::load,
                select = engine::select,
                newSite = engine::newSite,
                cancelNewSite = engine::cancelNewSite,
                setName = engine::setName,
                confirmTimeZone = engine::confirmTimeZone,
                changeTimeZone = engine::changeTimeZone,
                pickTimeZone = engine::pickTimeZone,
                submit = engine::submit,
                timeZones = ::availableTimeZones,
            )
    }
}

@StringRes
fun SiteRole.label(): Int =
    when (this) {
        SiteRole.Owner -> R.string.role_owner
        SiteRole.Administrator -> R.string.role_administrator
        SiteRole.Member -> R.string.role_member
    }

@StringRes
fun NameError.message(): Int =
    when (this) {
        NameError.Blank -> R.string.create_site_name_blank
        NameError.TooLong -> R.string.create_site_name_too_long
    }

/** Loading the Sites failed: Unreachable and Certificate reuse the sign-in copy. */
@StringRes
fun SitesNotice.loadMessage(): Int =
    when (this) {
        SitesNotice.Unreachable -> R.string.notice_unreachable

        SitesNotice.Certificate -> R.string.notice_certificate

        SitesNotice.IdentityProviderUnavailable,
        SitesNotice.KeyReused,
        SitesNotice.Unexpected,
        -> R.string.sites_unexpected
    }

/** Create Site failed: the Site was not created. */
@StringRes
fun SitesNotice.createMessage(): Int =
    when (this) {
        SitesNotice.Unreachable -> R.string.notice_unreachable
        SitesNotice.Certificate -> R.string.notice_certificate
        SitesNotice.IdentityProviderUnavailable -> R.string.create_site_unavailable
        SitesNotice.KeyReused -> R.string.create_site_key_reused
        SitesNotice.Unexpected -> R.string.create_site_unexpected
    }

@StringRes
fun FirstRunStep.label(): Int =
    when (this) {
        FirstRunStep.AddHub -> R.string.garden_step_add_hub
        FirstRunStep.AddNode -> R.string.garden_step_add_node
        FirstRunStep.Calibrate -> R.string.garden_step_calibrate
        FirstRunStep.SetLowThreshold -> R.string.garden_step_set_threshold
    }

@StringRes
fun StepState.label(): Int =
    when (this) {
        StepState.Next -> R.string.garden_step_next
        StepState.Later -> R.string.garden_step_later
        StepState.Done -> R.string.garden_step_done
    }

/** Pause and Resume name the Site (`%1$s`); Site settings does not. */
@StringRes
fun SiteMenuAction.label(): Int =
    when (this) {
        SiteMenuAction.Pause -> R.string.site_menu_pause
        SiteMenuAction.Resume -> R.string.site_menu_resume
        SiteMenuAction.SiteSettings -> R.string.site_menu_settings
    }
