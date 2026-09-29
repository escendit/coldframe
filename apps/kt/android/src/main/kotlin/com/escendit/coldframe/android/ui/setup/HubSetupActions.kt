package com.escendit.coldframe.android.ui.setup

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.setup.CodeError
import com.escendit.coldframe.core.setup.HubSetupEngine
import com.escendit.coldframe.core.setup.KeyNotice
import com.escendit.coldframe.core.setup.NetworkSecurity
import com.escendit.coldframe.core.setup.OutcomeAction
import com.escendit.coldframe.core.setup.ProgressSegment
import com.escendit.coldframe.core.setup.SetupStep
import com.escendit.coldframe.core.setup.SignalStrength
import com.escendit.coldframe.core.setup.WifiError

/**
 * What Add a Hub can ask of the core. The shell never runs BLE, crypto or the flow's rules; it
 * forwards taps to [HubSetupEngine] and renders its state (AD-14, AD-25).
 */
class HubSetupActions(
    val open: () -> Unit = {},
    val close: () -> Unit = {},
    val recheckRadio: () -> Unit = {},
    val announcing: (Boolean) -> Unit = {},
    val back: () -> Unit = {},
    val leave: () -> Unit = {},
    val confirmLeave: () -> Unit = {},
    val stayInFlow: () -> Unit = {},
    val select: (String) -> Unit = {},
    val continueFromScan: () -> Unit = {},
    val setCode: (String) -> Unit = {},
    val submitCode: () -> Unit = {},
    val continueFromCode: () -> Unit = {},
    val chooseNetwork: (String) -> Unit = {},
    val chooseOtherNetwork: () -> Unit = {},
    val setOtherSsid: (String) -> Unit = {},
    val setPassword: (String) -> Unit = {},
    val continueFromWifi: () -> Unit = {},
    val chooseSite: (String) -> Unit = {},
    val retryKey: () -> Unit = {},
    val start: () -> Unit = {},
    val outcomeAction: (OutcomeAction) -> Unit = {},
) {
    companion object {
        val None = HubSetupActions()

        fun of(engine: HubSetupEngine): HubSetupActions =
            HubSetupActions(
                open = engine::open,
                close = engine::close,
                recheckRadio = engine::recheckRadio,
                announcing = engine::announcing,
                back = engine::back,
                leave = engine::leave,
                confirmLeave = engine::confirmLeave,
                stayInFlow = engine::stayInFlow,
                select = engine::select,
                continueFromScan = engine::continueFromScan,
                setCode = engine::setCode,
                submitCode = engine::submitCode,
                continueFromCode = engine::continueFromCode,
                chooseNetwork = engine::chooseNetwork,
                chooseOtherNetwork = engine::chooseOtherNetwork,
                setOtherSsid = engine::setOtherSsid,
                setPassword = engine::setPassword,
                continueFromWifi = engine::continueFromWifi,
                chooseSite = engine::chooseSite,
                retryKey = engine::retryKey,
                start = engine::start,
                outcomeAction = engine::outcomeAction,
            )
    }
}

@StringRes
fun SignalStrength.label(): Int =
    when (this) {
        SignalStrength.Strong -> R.string.add_hub_signal_strong
        SignalStrength.Medium -> R.string.add_hub_signal_medium
        SignalStrength.Weak -> R.string.add_hub_signal_weak
    }

@StringRes
fun NetworkSecurity.label(): Int =
    when (this) {
        NetworkSecurity.Open -> R.string.add_hub_security_open
        NetworkSecurity.Wpa2 -> R.string.add_hub_security_wpa2
        NetworkSecurity.Wpa3Transition -> R.string.add_hub_security_wpa3_transition
        NetworkSecurity.Wpa3Only -> R.string.add_hub_security_wpa3_only
        NetworkSecurity.Other -> R.string.add_hub_security_other
    }

/** The step title, a headline; step 5 names the Hub (`%1$s`). */
@StringRes
fun SetupStep.title(): Int =
    when (this) {
        SetupStep.Scan -> R.string.add_hub_title_scan
        SetupStep.Code -> R.string.add_hub_title_code
        SetupStep.Wifi -> R.string.add_hub_title_wifi
        SetupStep.Site -> R.string.add_hub_title_site
        SetupStep.Progress -> R.string.add_hub_title_progress
    }

@StringRes
fun CodeError.message(): Int =
    when (this) {
        CodeError.Blank -> R.string.add_hub_code_blank
        CodeError.WrongCode -> R.string.add_hub_code_wrong
    }

@StringRes
fun WifiError.message(): Int =
    when (this) {
        WifiError.SsidBlank -> R.string.add_hub_wifi_ssid_blank
        WifiError.SsidTooLong -> R.string.add_hub_wifi_ssid_too_long
        WifiError.PasswordTooLong -> R.string.add_hub_wifi_password_too_long
    }

@StringRes
fun KeyNotice.message(): Int =
    when (this) {
        KeyNotice.Unreachable -> R.string.notice_unreachable
        KeyNotice.Certificate -> R.string.notice_certificate
        KeyNotice.Unexpected -> R.string.add_hub_key_unexpected
    }

@StringRes
fun ProgressSegment.label(): Int =
    when (this) {
        ProgressSegment.Bluetooth -> R.string.add_hub_segment_bluetooth
        ProgressSegment.WifiSent -> R.string.add_hub_segment_wifi_sent
        ProgressSegment.Joining -> R.string.add_hub_segment_joining
        ProgressSegment.Server -> R.string.add_hub_segment_server
    }

/** What the progress element says it is doing while [this] is active. */
@StringRes
fun ProgressSegment?.activity(): Int =
    when (this) {
        ProgressSegment.Bluetooth -> R.string.add_hub_activity_bluetooth
        ProgressSegment.WifiSent -> R.string.add_hub_activity_wifi_sent
        ProgressSegment.Joining -> R.string.add_hub_activity_joining
        ProgressSegment.Server -> R.string.add_hub_activity_server
        null -> R.string.add_hub_activity_done
    }

@StringRes
fun OutcomeAction.label(): Int =
    when (this) {
        OutcomeAction.AddNode -> R.string.add_hub_add_node
        OutcomeAction.ReenterPassword -> R.string.add_hub_reenter_password
        OutcomeAction.OtherNetwork -> R.string.add_hub_other_network
        OutcomeAction.ChooseNetwork -> R.string.add_hub_choose_network
        OutcomeAction.RetryWifi -> R.string.add_hub_retry_wifi
        OutcomeAction.Help -> R.string.add_hub_help
        OutcomeAction.StartOver -> R.string.add_hub_start_over
        OutcomeAction.Close -> R.string.add_hub_close
    }
