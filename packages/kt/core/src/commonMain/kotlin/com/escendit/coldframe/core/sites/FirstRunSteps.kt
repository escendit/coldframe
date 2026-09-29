package com.escendit.coldframe.core.sites

/** The four first-run steps of an empty Site, in order (UX-DR54). */
public enum class FirstRunStep {
    AddHub,
    AddNode,
    Calibrate,
    SetLowThreshold,
}

/** How a step tile is drawn: next (solid primary), later (dashed) or done (checkmark). */
public enum class StepState {
    Next,
    Later,
    Done,
}

public data class FirstRunTile(
    val step: FirstRunStep,
    /** STEP 1 to 4. */
    val number: Int,
    val state: StepState,
)

/**
 * The first-run tiles of an empty Site. [actionable] tiles start their flow (mobile, Admin+, once
 * the flow exists); [memberNotice] adds "Only Owners and Administrators can add Devices."
 */
public data class FirstRunSteps(
    val tiles: List<FirstRunTile>,
    val actionable: Boolean,
    val memberNotice: Boolean,
) {
    public companion object {
        /**
         * The tiles for [role]. No Hub or Node exists in Story 1.8, so Add a Hub is next and the
         * rest are later. [flowAvailable] turns on with the Add a Hub flow (a later epic).
         */
        public fun of(
            role: SiteRole,
            flowAvailable: Boolean = false,
        ): FirstRunSteps =
            FirstRunSteps(
                tiles =
                    FirstRunStep.entries.mapIndexed { index, step ->
                        FirstRunTile(step, index + 1, if (index == 0) StepState.Next else StepState.Later)
                    },
                actionable = flowAvailable && role >= SiteRole.Administrator,
                memberNotice = role == SiteRole.Member,
            )
    }
}
