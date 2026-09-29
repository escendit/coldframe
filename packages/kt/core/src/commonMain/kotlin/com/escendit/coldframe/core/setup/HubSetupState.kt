package com.escendit.coldframe.core.setup

/** The five steps of Add a Hub (UX-DR66), "01 / 05" to "05 / 05". */
public enum class SetupStep {
    Scan,
    Code,
    Wifi,
    Site,
    Progress,
    ;

    /** 1 to 5. */
    public val number: Int get() = ordinal + 1

    public companion object {
        public const val COUNT: Int = 5
    }
}

/** How strong a candidate's signal is, from its RSSI; spoken and shown in words. */
public enum class SignalStrength {
    Strong,
    Medium,
    Weak,
    ;

    public companion object {
        public fun of(rssi: Int): SignalStrength =
            when {
                rssi >= -65 -> Strong
                rssi >= -80 -> Medium
                else -> Weak
            }
    }
}

/**
 * A Hub in BLE range (UX-DR37): [shortId] is the four digits of its label ("3F2A"), from the
 * advertised name; [peripheralId] is never shown.
 */
public data class HubCandidate(
    val peripheralId: String,
    val shortId: String,
    val rssi: Int,
) {
    val signal: SignalStrength get() = SignalStrength.of(rssi)
}

/** The one-line reason under the setup code field. */
public enum class CodeError {
    /** Nothing to check. */
    Blank,

    /** The first sealed reply did not open (UX-DR95). */
    WrongCode,
}

/** Step 2: the code as typed, kept for re-entry after a wrong code. */
public data class CodeForm(
    val text: String,
    val error: CodeError?,
    val working: Boolean,
    val accepted: Boolean,
) {
    /** Never prints the code (it salts the session key). */
    override fun toString(): String = "CodeForm(text=<redacted>, error=$error, working=$working, accepted=$accepted)"
}

/** What the Hub said about itself once the code was accepted; [deviceId] is the full lowercase hex. */
public data class HubIdentity(
    val deviceId: String,
    val firmwareVersion: String,
)

/** How a network authenticates, as the Hub heard it. */
public enum class NetworkSecurity {
    Open,
    Wpa2,
    Wpa3Transition,
    Wpa3Only,
    Other,
    ;

    /** WPA3-only and other networks are shown but never selectable (UX-DR42). */
    public val supported: Boolean get() = this != Wpa3Only && this != Other
}

/** One Wi-Fi network row (UX-DR42), strongest first, as the Hub sent them. */
public data class WifiNetworkRow(
    val ssid: String,
    val security: NetworkSecurity,
    val rssi: Int,
)

/** The one-line reason under a Wi-Fi field. */
public enum class WifiError {
    /** No network chosen or typed. */
    SsidBlank,

    /** Longer than 32 bytes. */
    SsidTooLong,

    /** Longer than 64 bytes. */
    PasswordTooLong,
}

/**
 * Step 3: [networks] is null until the Hub's scan list arrives. [ssid] is the chosen row, or
 * the typed SSID when [other] ("Other network") is on. The password is dropped when the flow
 * ends and is never logged.
 */
public data class WifiForm(
    val networks: List<WifiNetworkRow>?,
    val ssid: String,
    val other: Boolean,
    val password: String,
    val error: WifiError?,
) {
    /** Never prints the password. */
    override fun toString(): String =
        "WifiForm(networks=$networks, ssid=$ssid, other=$other, password=<redacted>, error=$error)"

    /** The chosen network's security; an open network needs no password. */
    val chosenSecurity: NetworkSecurity?
        get() = if (other) null else networks?.firstOrNull { it.ssid == ssid }?.security

    public companion object {
        public val EMPTY: WifiForm = WifiForm(networks = null, ssid = "", other = false, password = "", error = null)
    }
}

/** A Site the user may add a Hub to (Administrator or Owner). */
public data class SiteChoice(
    val id: String,
    val name: String,
)

/** Reading the enrolment key on step 4. */
public enum class KeyNotice {
    Unreachable,
    Certificate,
    Unexpected,
}

/**
 * Step 4: the Site and the Server's enrolment key. [fingerprint] is shown once the key is read
 * and checked; [notice] replaces it when it could not be read.
 */
public data class SiteForm(
    val sites: List<SiteChoice>,
    val selectedId: String?,
    val fingerprint: String?,
    val loadingKey: Boolean,
    val notice: KeyNotice?,
)

/** One segment of the Setup progress (UX-DR40). */
public enum class ProgressSegment {
    Bluetooth,
    WifiSent,
    Joining,
    Server,
}

public enum class SegmentState {
    Done,
    Active,
    Pending,
}

/**
 * Step 5 (UX-DR40): [reached] is how many segments are done; the next one is active. Segments
 * advance only on real events: enrolment 201 (Bluetooth), the `WifiConfig` write (Wi-Fi sent),
 * `WifiResult CONNECTED` (Joining and Server). [elapsedSeconds] ticks every second.
 */
public data class ProgressState(
    val reached: Int,
    val elapsedSeconds: Int,
) {
    public fun stateOf(segment: ProgressSegment): SegmentState =
        when {
            segment.ordinal < reached -> SegmentState.Done
            segment.ordinal == reached -> SegmentState.Active
            else -> SegmentState.Pending
        }

    /** The active segment, or null once every segment is done. */
    val active: ProgressSegment? get() = ProgressSegment.entries.getOrNull(reached)
}

/** Every way the flow ends on an outcome screen (UX-DR55). */
public enum class OutcomeKind {
    /** "Hub is online", with ADD A NODE. */
    Online,

    /** `WifiResult WRONG_PASSWORD`: Re-enter password / Other network. */
    WrongPassword,

    /** `WifiResult NETWORK_NOT_FOUND`: back to step 3. */
    NetworkNotFound,

    /** `WifiResult UNSUPPORTED_SECURITY`: back to step 3. */
    UnsupportedSecurity,

    /** `WifiResult NO_SERVER`: Try again (resend `WifiConfig`) / Help. */
    NoServer,

    /** No `WifiResult` 90 s after `WifiConfig`: Try again from step 1. */
    Timeout,

    /** The BLE link dropped before an outcome: Try again from step 1. */
    LostConnection,

    /** 409: the Hub is enrolled on another Site. */
    OnAnotherSite,

    /** 403: the caller may not add Devices to the Site. */
    NotAllowed,

    /** 404: the Site is gone. */
    SiteGone,

    /** 5xx or no answer from the Server during enrolment. */
    ServerUnreachable,

    /** The key's SHA-256 is not its fingerprint, or the Hub said `FINGERPRINT_MISMATCH`. */
    FingerprintMismatch,

    /** The Hub answered a `SetupError` or something the flow did not expect. */
    HubRefused,
}

/** The primary and secondary actions of an outcome screen. */
public enum class OutcomeAction {
    /** Closes the flow to the Garden (the Node flow arrives in Epic 4). */
    AddNode,

    /** Back to step 3 with the network kept and the password cleared. */
    ReenterPassword,

    /** Back to step 3 with "Other network" open. */
    OtherNetwork,

    /** Back to step 3 to pick another network. */
    ChooseNetwork,

    /** Resends only `WifiConfig` on the open session. */
    RetryWifi,

    /** Reveals the troubleshooting text. */
    Help,

    /** Back to step 1 with a new session. */
    StartOver,

    /** Closes the flow. */
    Close,
}

public data class SetupOutcome(
    val kind: OutcomeKind,
    /** Whether Help has been opened (NO_SERVER). */
    val helpShown: Boolean = false,
) {
    val success: Boolean get() = kind == OutcomeKind.Online

    /** The eyebrow of an error outcome: the step it stopped on ("STEP 5 STOPPED"). */
    val stoppedStep: Int get() = SetupStep.Progress.number

    val primary: OutcomeAction
        get() =
            when (kind) {
                OutcomeKind.Online -> OutcomeAction.AddNode

                OutcomeKind.WrongPassword -> OutcomeAction.ReenterPassword

                OutcomeKind.NetworkNotFound, OutcomeKind.UnsupportedSecurity -> OutcomeAction.ChooseNetwork

                OutcomeKind.NoServer -> OutcomeAction.RetryWifi

                OutcomeKind.Timeout,
                OutcomeKind.LostConnection,
                OutcomeKind.ServerUnreachable,
                -> OutcomeAction.StartOver

                OutcomeKind.OnAnotherSite,
                OutcomeKind.NotAllowed,
                OutcomeKind.SiteGone,
                OutcomeKind.FingerprintMismatch,
                OutcomeKind.HubRefused,
                -> OutcomeAction.Close
            }

    val secondary: OutcomeAction?
        get() =
            when (kind) {
                OutcomeKind.WrongPassword -> OutcomeAction.OtherNetwork
                OutcomeKind.NoServer -> if (helpShown) null else OutcomeAction.Help
                else -> null
            }
}

/** A screen-reader announcement the shells make once per [id] (UX-DR105). */
public enum class AnnouncementKind {
    /** Polite, once per Device: "Hub 3F2A found, strong signal." */
    CandidateFound,

    /** Polite: "Wi-Fi sent. Joining Novak-Home." (both segments move on the same write). */
    WifiSent,

    /** Polite: "Server sees Hub 3F2A." */
    ServerSees,

    /** Assertive: the wrong-code reason. */
    WrongCode,

    /** Assertive: an error outcome's headline and next action. */
    Error,
}

public data class SetupAnnouncement(
    val id: Int,
    val kind: AnnouncementKind,
    val assertive: Boolean,
    /** The Hub's four digits, the SSID, or the outcome, as the copy needs. */
    val hub: String?,
    val ssid: String?,
    val signal: SignalStrength?,
    val outcome: OutcomeKind?,
)

/**
 * Add a Hub as the shells render it (UX-DR66). [open] is false while the flow is closed.
 * [outcome] replaces the step with an outcome screen. [confirmingLeave] shows "Stop setting up
 * Hub 3F2A? Nothing is saved on the Hub." [keepAwake] holds the screen on while the flow is open.
 */
public data class HubSetupState(
    val open: Boolean,
    val step: SetupStep,
    val radio: RadioState,
    val candidates: List<HubCandidate>,
    val noHubYet: Boolean,
    val selected: HubCandidate?,
    val code: CodeForm,
    val identity: HubIdentity?,
    val wifi: WifiForm,
    val site: SiteForm,
    val progress: ProgressState,
    val outcome: SetupOutcome?,
    val confirmingLeave: Boolean,
    val announcement: SetupAnnouncement?,
    /** The Server's host name, for the `NO_SERVER` help text only. */
    val serverHost: String = "",
) {
    val keepAwake: Boolean get() = open

    /** Cancel on step 1, Back afterwards (UX-DR39); an outcome screen has neither. */
    val showsCancel: Boolean get() = step == SetupStep.Scan

    /** The Hub's label name ("3F2A") once one is selected. */
    val hubId: String? get() = selected?.shortId

    public companion object {
        public val CLOSED: HubSetupState =
            HubSetupState(
                open = false,
                step = SetupStep.Scan,
                radio = RadioState.Ready,
                candidates = emptyList(),
                noHubYet = false,
                selected = null,
                code = CodeForm("", error = null, working = false, accepted = false),
                identity = null,
                wifi = WifiForm.EMPTY,
                site = SiteForm(emptyList(), selectedId = null, fingerprint = null, loadingKey = false, notice = null),
                progress = ProgressState(reached = 0, elapsedSeconds = 0),
                outcome = null,
                confirmingLeave = false,
                announcement = null,
            )
    }
}
