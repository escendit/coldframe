package com.escendit.coldframe.android.ui.setup

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.setup.CodeError
import com.escendit.coldframe.core.setup.LotPickerNoticeKind
import com.escendit.coldframe.core.setup.NodeOutcomeAction
import com.escendit.coldframe.core.setup.NodeSetupEngine
import com.escendit.coldframe.core.setup.NodeSetupStep

/**
 * What Add a Node can ask of the core. The shell never runs BLE, crypto or the flow's rules; it
 * forwards taps to [NodeSetupEngine] and renders its state (AD-14, AD-25). [open] takes the Lot
 * of a *no Node* tile, or null from Devices.
 */
class NodeSetupActions(
    val open: (lotId: String?) -> Unit = {},
    val close: () -> Unit = {},
    val recheckRadio: () -> Unit = {},
    val announcing: (Boolean) -> Unit = {},
    val back: () -> Unit = {},
    val leave: () -> Unit = {},
    val confirmLeave: () -> Unit = {},
    val stayInFlow: () -> Unit = {},
    val continueFromPress: () -> Unit = {},
    val select: (String) -> Unit = {},
    val continueFromScan: () -> Unit = {},
    val setCode: (String) -> Unit = {},
    val submitCode: () -> Unit = {},
    val continueFromCode: () -> Unit = {},
    val retryLots: () -> Unit = {},
    val chooseLot: (String) -> Unit = {},
    val openNewLot: () -> Unit = {},
    val setNewLotName: (String) -> Unit = {},
    val createLot: () -> Unit = {},
    val assign: () -> Unit = {},
    val outcomeAction: (NodeOutcomeAction) -> Unit = {},
) {
    companion object {
        val None = NodeSetupActions()

        fun of(engine: NodeSetupEngine): NodeSetupActions =
            NodeSetupActions(
                open = { lotId -> engine.open(lotId = lotId) },
                close = engine::close,
                recheckRadio = engine::recheckRadio,
                announcing = engine::announcing,
                back = engine::back,
                leave = engine::leave,
                confirmLeave = engine::confirmLeave,
                stayInFlow = engine::stayInFlow,
                continueFromPress = engine::continueFromPress,
                select = engine::select,
                continueFromScan = engine::continueFromScan,
                setCode = engine::setCode,
                submitCode = engine::submitCode,
                continueFromCode = engine::continueFromCode,
                retryLots = engine::retryLots,
                chooseLot = engine::chooseLot,
                openNewLot = engine::openNewLot,
                setNewLotName = engine::setNewLotName,
                createLot = engine::createLot,
                assign = engine::assign,
                outcomeAction = engine::outcomeAction,
            )
    }
}

/** The step title, a headline. The outcome step has its own screen. */
@StringRes
fun NodeSetupStep.title(): Int =
    when (this) {
        NodeSetupStep.Press -> R.string.add_node_title_press
        NodeSetupStep.Scan -> R.string.add_node_title_scan
        NodeSetupStep.Code -> R.string.add_node_title_code
        NodeSetupStep.Lot, NodeSetupStep.Outcome -> R.string.add_node_title_lot
    }

/** The reason under the Node's setup code field; the wrong-code one names the Node (`%1$s`). */
@StringRes
fun CodeError.nodeMessage(): Int =
    when (this) {
        CodeError.Blank -> R.string.add_node_code_blank
        CodeError.WrongCode -> R.string.add_node_code_wrong
    }

/** The notice on step 4; the Lot-taken one names the Lot (`%1$s`). */
@StringRes
fun LotPickerNoticeKind.message(): Int =
    when (this) {
        LotPickerNoticeKind.Unreachable -> R.string.notice_unreachable
        LotPickerNoticeKind.Certificate -> R.string.notice_certificate
        LotPickerNoticeKind.Unexpected -> R.string.add_node_lots_unexpected
        LotPickerNoticeKind.LotTaken -> R.string.add_node_lot_taken
        LotPickerNoticeKind.LotGone -> R.string.add_node_lot_gone
        LotPickerNoticeKind.AssignFailed -> R.string.add_node_assign_failed
    }

@StringRes
fun NodeOutcomeAction.label(): Int =
    when (this) {
        NodeOutcomeAction.Done -> R.string.add_node_done
        NodeOutcomeAction.StartOver -> R.string.add_node_start_over
        NodeOutcomeAction.Close -> R.string.add_node_close
        NodeOutcomeAction.Calibrate -> R.string.calibrate_action
    }
